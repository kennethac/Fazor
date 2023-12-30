using System;
using System.Collections.Generic;
using Sprache;
using System.Linq;

namespace FazorGenerator.Parser.Parameters;

public record FazorParamInfo(string Name, string Type);

public class SpracheParser
{
    public static readonly Parser<IEnumerable<Parameter>> ParseParameters =
        ParameterList.Token().End();

    static Parser<T> Token<T>(Parser<T> parser) =>
        Parse.WhiteSpace.Many().Then(_ => parser).Then(item => Parse.WhiteSpace.Many().Return(item));

    static Parser<IEnumerable<char>> TokenString(string text) =>
        Token(Parse.String(text).Then(a => Parse.WhiteSpace.Many().Return(a)));

    static Parser<char> TokenChar(char c) => Token(Parse.Char(c));

    static Parser<T> BracketWrapper<T>(Parser<T> parser) =>
        TokenChar('[').Then(_ => parser).Then(content => TokenChar(']').Return(content));

    static Parser<IEnumerable<T>> List<T>(Parser<T> parser) =>
        parser.DelimitedBy(TokenChar(','));

    static Parser<IOption<T>> Optional<T>(Parser<T> parser) =>
        parser.Optional();

    static Parser<Parameter> Parameter =>
        Optional(BracketWrapper(List(Attribute)))
            .Then(attributes => Type2
                .Then(type => Identifier.Token()
                    .Then(identifier => Optional(DefaultValue.Token())
                        .Select(defaultValue => new Parameter(attributes, type,
                            identifier.Substring(0, 1).ToUpper() + identifier.Substring(1), defaultValue)))));

    static Parser<IEnumerable<Parameter>> ParameterList =>
        List(Parameter);

    static Parser<string> Attribute =>
        BracketWrapper(Parse.CharExcept(']').Many()).Text();

    static Parser<string> Identifier =>
        Parse.Letter.Or(Parse.Char('_'))
            .Then(first => Parse.LetterOrDigit.Or(Parse.Char('_')).Many()
                .Select(rest => new string(new[] { first }.Concat(rest).ToArray())));

    static Parser<string> Number =>
        Parse.Number.Text();

    static Parser<string> StringLiteral =>
        Parse.Char('"').Then(_ =>
                Parse.CharExcept(c => c != '"' && c != '\\', "\"").Or(Parse.Char('\\').Then(_ => Parse.AnyChar)))
            .Many().Text().Contained(Parse.Char('"'), Parse.Char('"'));

    static Parser<string> DefaultValue =>
        Identifier.Or(Number).Or(StringLiteral).Or(TokenString("null").Return("null"));

    static Parser<T> Bracket<T>(Parser<T> parser) =>
        TokenChar('<').Then(_ => parser).Then(content => TokenChar('>').Return(content));

    static Parser<string> Type2 =>
        Parse.Ref(() =>
            Identifier
                .Then(identifier =>
                    Bracket(Parse.Ref(() => List(Type2)))
                        .Select(genericParams => $"{identifier}<{string.Join(", ", genericParams)}>")
                        .Or(Parse.Return(identifier))));
}

public class Parameter(
    IOption<IEnumerable<string>> attributes,
    string type,
    string identifier,
    IOption<string> defaultValue)
{
    public IOption<IEnumerable<string>> Attributes { get; } = attributes;
    public string Type { get; } = type;
    public string Identifier { get; } = identifier;
    public IOption<string> DefaultValue { get; } = defaultValue;
}

/*
public class ParameterVisitor : CSharpParameterBaseVisitor<object>
{
    public readonly List<FazorParamInfo> Params = new();

    public override object VisitParameter(CSharpParameterParser.ParameterContext context)
    {
        var parameterName = context.Identifier().GetText();
        var type = context.type().GetText();
        Params.Add(new(parameterName, type));
        return base.VisitParameter(context);
    }
}
//*/