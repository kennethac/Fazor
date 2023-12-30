using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FazorGenerator.Parser.Parameters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Sprache;

namespace FazorGenerator;

[Generator]
public class FazorSourceGenerator : IIncrementalGenerator
{
    private static readonly Regex NaiveRegex = new(@"private\s+RenderFragment\sFazorRender\((.*?)\)");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var additionalTexts =
            context
                .AdditionalTextsProvider
                .Where(static (file)
                    => file.Path.EndsWith("razor", StringComparison.OrdinalIgnoreCase))
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Combine(context.CompilationProvider);
        context.RegisterSourceOutput(additionalTexts,
            (a, pair) => ProcessRazorFiles(a, pair.Left.Right, pair.Left.Left, pair.Right));
    }

    private static void ProcessRazorFiles2(
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider,
        AdditionalText additionalTexts,
        Compilation compilation)
    {
    }

    private static void ProcessRazorFiles(
        SourceProductionContext context,
        AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider,
        AdditionalText additionalTexts,
        Compilation compilation)
    {
        var projectDirectory = Path.GetDirectoryName(compilation.SyntaxTrees.First().FilePath);
        var sourceText = additionalTexts.GetText()?.ToString();
        var fileName = Path.GetFileNameWithoutExtension(additionalTexts.Path);

        if (sourceText is null || !sourceText.Contains("@inherits Fazor.FazorComponent"))
        {
            return;
        }

        if (!analyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace",
                out var rootNamespace))
        {
            rootNamespace = "ASP";
        }

        var text = analyzerConfigOptionsProvider.GetOptions(additionalTexts);
        if (!text.TryGetValue("build_metadata.AdditionalFiles.TargetPath", out var val))
        {
            return;
        }

        var namespaceParts =
            Path.GetDirectoryName(additionalTexts.Path)!
                .Replace(projectDirectory ?? string.Empty, "")
                .Trim('/', '\\')
                .Split(Path.DirectorySeparatorChar)
                .Select(piece => piece.Replace(".", "_"));

        var componentNamespace = string.Join(".", [rootNamespace, ..namespaceParts]);

        var matchingMethods = NaiveRegex.Matches(sourceText);

        if (matchingMethods.Count == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "FZ001",
                "None",
                "There was no RenderMethod found!",
                DiagnosticSeverity.Error,
                DiagnosticSeverity.Error,
                true,
                0));
            return;
        }

        if (matchingMethods.Count > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                "FZ002",
                "None",
                "There were too many RenderMethod methods found!",
                DiagnosticSeverity.Error,
                DiagnosticSeverity.Error,
                true,
                0));
            return;
        }

        var arguments = matchingMethods[0].Groups[1].Value;
        var toCreateResult = SpracheParser.ParseParameters(new Input(arguments));

        if (toCreateResult is not { WasSuccessful: true, Value: var toCreate })
        {
            return;
        }

        var toCreateArray = toCreate as Parameter[] ?? toCreate.ToArray();
        var parameters = toCreateArray.Select(parameter =>
        {
            var propertyDeclaration = SyntaxFactory.PropertyDeclaration(
                    SyntaxFactory.ParseTypeName(parameter.Type),
                    SyntaxFactory.Identifier(parameter.Identifier))
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAttributeLists(SyntaxFactory.SingletonList(
                    SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Attribute(SyntaxFactory.ParseName("Parameter"))
                    ))
                ))
                .WithAccessorList(SyntaxFactory.AccessorList(
                    SyntaxFactory.List([
                        SyntaxFactory
                            .AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory
                            .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    ])
                ));
            return propertyDeclaration;
        });

        var invokeFazorArgs = toCreateArray.Select(parameter =>
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Identifier)));

        var invokeFazorMethod = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.ParseTypeName("RenderFragment"),
                SyntaxFactory.Identifier("GetRenderFragment"))
            .WithModifiers(SyntaxFactory.TokenList(
            [
                SyntaxFactory.Token(SyntaxKind.ProtectedKeyword),
                SyntaxFactory.Token(SyntaxKind.OverrideKeyword)
            ]))
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.SingletonList<StatementSyntax>(
                    SyntaxFactory.ReturnStatement(
                        SyntaxFactory.InvocationExpression(
                                SyntaxFactory.ParseExpression("FazorRender"))
                            .WithArgumentList(SyntaxFactory.ArgumentList(
                                SyntaxFactory.SeparatedList(
                                    invokeFazorArgs
                                )
                            ))
                    )
                )
            ));

        var classDeclaration = SyntaxFactory.ClassDeclaration(fileName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
            .WithMembers(new SyntaxList<MemberDeclarationSyntax>(SyntaxFactory.List<MemberDeclarationSyntax>(parameters)
                .Append(invokeFazorMethod).ToList()));

        // Add namespace and using directives
        var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(componentNamespace))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classDeclaration));

        // Create a compilation unit with the namespace declaration
        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(SyntaxFactory.List([
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic")),
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Microsoft.AspNetCore.Components")),
            ]))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(namespaceDeclaration));

        context.AddSource($"{fileName}.g.cs", compilationUnit.NormalizeWhitespace().GetText(Encoding.UTF8));
        //*/
    }

    /// <summary>
    /// Generate code action.
    /// It will be executed on specific nodes (ClassDeclarationSyntax annotated with the [Report] attribute) changed by the user.
    /// </summary>
    /// <param name="context">Source generation context used to add source files.</param>
    /// <param name="compilation">Compilation used to provide access to the Semantic Model.</param>
    /// <param name="classDeclarations">Nodes annotated with the [Report] attribute that trigger the generate action.</param>
    private void GenerateCode(SourceProductionContext context, Compilation compilation,
        IEnumerable<ClassDeclarationSyntax> classDeclarations)
    {
        var symbols =
            classDeclarations
                .Select(d => compilation.GetSemanticModel(d.SyntaxTree))
                .Select(d => d);

        foreach (var classDeclarationSyntax in classDeclarations)
        {
            // We need to get semantic model of the class to retrieve metadata.
            var semanticModel = compilation.GetSemanticModel(classDeclarationSyntax.SyntaxTree);

            // Symbols allow us to get the compile-time information.
            if (ModelExtensions.GetDeclaredSymbol(semanticModel, classDeclarationSyntax) is not INamedTypeSymbol
                classSymbol)
                continue;

            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // 'Identifier' means the token of the node. Get class name from the syntax node.
            var className = classDeclarationSyntax.Identifier.Text;

            // Go through all class members with a particular type (property) to generate method lines.
            var methodBody = classSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Select(p =>
                    $@"        yield return $""{p.Name}:{{this.{p.Name}}}"";"); // e.g. yield return $"Id:{this.Id}";

            // Build up the source code
            var code = $@"// <auto-generated/>

using System;
using System.Collections.Generic;

namespace {namespaceName};

partial class {className}
{{
    public IEnumerable<string> Report()
    {{
{string.Join("\n", methodBody)}
    }}
}}
";

            // Add the source code to the compilation.
            context.AddSource($"{className}.g.cs", SourceText.From(code, Encoding.UTF8));
        }
    }
}