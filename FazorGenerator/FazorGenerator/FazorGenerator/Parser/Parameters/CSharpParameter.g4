grammar CSharpParameter;

// Parser rules
parse: parameterList EOF;

parameterList: parameter (',' parameter)*;

parameter: attributes? type Identifier ('=' defaultValue)?;

attributes: '[' Attribute (',' Attribute)* ']';

type: Identifier;

defaultValue: (Identifier | Number | StringLiteral | 'null');

// Lexer rules
Attribute: '[' ~[\]\r\n]* ']';
Identifier: [a-zA-Z_] [a-zA-Z_0-9]*;
Number: [0-9]+;
StringLiteral: '"' ( ~["\\] | '\\' . )* '"';

// Skip whitespaces and commas
WS: [ \t\r\n]+ -> skip;
COMMA: ',';
