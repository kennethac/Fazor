using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
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

/// <summary>
/// A sample source generator that creates a custom report based on class properties. The target class should be annotated with the 'Generators.ReportAttribute' attribute.
/// When using the source code as a baseline, an incremental source generator is preferable because it reduces the performance overhead.
/// </summary>
[Generator]
public class SampleIncrementalSourceGenerator : IIncrementalGenerator
{
    private static readonly Regex NaiveRegex = new(@"protected\s+RenderFragment\sFazorRender\((.*?)\)");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var additionalTexts =
            context
                .AdditionalTextsProvider
                .Where(static (file)
                    => file.Path.EndsWith("razor", StringComparison.OrdinalIgnoreCase))
                .Combine(context.AnalyzerConfigOptionsProvider);
        context.RegisterSourceOutput(additionalTexts,
            (a, pair) => ProcessRazorFiles(a, pair.Right, pair.Left));

        // Filter classes annotated with the [Report] attribute. Only filtered Syntax Nodes can trigger code generation.
        // var provider = context.SyntaxProvider
        // .CreateSyntaxProvider(
        // (s, _) => s is ClassDeclarationSyntax,
        // (d, _) => d)
        // .Select((t, _) => t);

        // Generate the source code.
        // context.RegisterSourceOutput(context.CompilationProvider.Combine(provider.Collect()),
        // ((ctx, t) => GenerateCode(ctx, t.Left, t.Right.OfType<ClassDeclarationSyntax>())));
    }

    private void ProcessRazorFiles(SourceProductionContext context,
        AnalyzerConfigOptionsProvider analyzerConfigOptionsProvider,
        AdditionalText additionalTexts)
    {
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

        var namespaceParts = additionalTexts
            .Path
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
                    SyntaxFactory.SingletonList(SyntaxFactory
                        .AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)))
                ));
            return propertyDeclaration;
        });

        var invokeFazorArgs = toCreateArray.Select(parameter =>
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Identifier)));

        var invokeFazorMethod = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                SyntaxFactory.Identifier("InvokeFazor"))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.SingletonList<StatementSyntax>(
                    SyntaxFactory.ExpressionStatement(
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
            .WithMembers((SyntaxList<MemberDeclarationSyntax>)SyntaxFactory.List<MemberDeclarationSyntax>(parameters)
                .Append((MethodDeclarationSyntax)invokeFazorMethod));

        // Add namespace and using directives
        var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(rootNamespace))
            .WithUsings(SyntaxFactory.SingletonList(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Fazor"))
            ))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(classDeclaration));

        // Create a compilation unit with the namespace declaration
        var compilationUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(SyntaxFactory.SingletonList(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic"))
            ))
            .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(namespaceDeclaration));

        context.AddSource($"{fileName}.g.cs", compilationUnit.GetText());
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