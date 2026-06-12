grammar Ocl;

// ---------------------------------------------------------------------------
// OCL subset grammar for OclNet — Phase 1 (the PURE rule class of the
// VDI 3682 Blatt 3 catalogue: type ops, collections, boolean logic,
// comparison, arithmetic, property navigation, let-in, Sequence literals).
//
// Not yet in scope: closure()/closureDepth(), oclAsType, tuple types.
// The single left-recursive `expression` rule encodes operator precedence by
// alternative order (ANTLR4 resolves direct left recursion accordingly).
// ---------------------------------------------------------------------------

// A rule file is one or more invariants and/or operation definitions.
file_ : unit+ EOF ;

// Anchored single-expression entry point: without the EOF a trailing-garbage
// input like "true nad false" would silently parse as just "true".
exprEof : expression EOF ;

unit : constraint | operationDef ;

constraint
    : 'context' typeName 'inv' name=IDENT? ':' expression
    ;

// A user-defined helper operation (OCL `def:`), e.g.
//   context Bounds def: isWithin(other: Bounds): Boolean = <expr>
operationDef
    : 'context' ctx=typeName 'def' ':' IDENT '(' paramList? ')' ':' ret=typeName '=' expression
    ;

paramList : param (',' param)* ;
param     : IDENT ':' typeName ;

// Precedence is encoded by alternative order (highest first). Navigation/calls bind
// tightest; `let`/`if` bind loosest so their trailing body extends as far right as
// possible (e.g. `let x = e in a.b().c` has the whole `a.b().c` as the body).
expression
    : expression '.' IDENT '(' argList? ')'                               # dotOpCall
    | expression '.' IDENT                                                # dotNav
    | expression '->' IDENT '(' iterBody? ')'                             # arrowCall
    | op=('not' | '-') expression                                         # unaryExpr
    | expression op=('*' | '/' | 'mod') expression                        # mulExpr
    | expression op=('+' | '-') expression                                # addExpr
    | expression op=('<' | '<=' | '>' | '>=') expression                  # relExpr
    | expression op=('=' | '<>') expression                               # eqExpr
    | expression op='and' expression                                      # andExpr
    | expression op='or' expression                                       # orExpr
    | expression op='xor' expression                                      # xorExpr
    | <assoc=right> expression 'implies' expression                       # impliesExpr
    | 'let' IDENT (':' typeName)? '=' expression 'in' expression          # letExpr
    | 'if' expression 'then' expression 'else' expression 'endif'         # ifExpr
    | primary                                                             # primaryExpr
    ;

// Arguments to a '.'-style operation call: e.oclIsKindOf(T), e.foo(a, b).
argList : expression (',' expression)* ;

// Body of a '->'-style collection call. Either an iterator form with one or
// more loop variables (forAll(c1, c2 | ...), select(e | ...)) or a plain
// argument (includes(x)) or nothing (size(), asSet()).
iterBody
    : iterVars '|' expression     # iteratorBody
    | argList                     # simpleArgs
    ;

iterVars : IDENT (',' IDENT)* (':' typeName)? ;

primary
    : literal                                       # literalPrimary
    | 'self'                                         # selfPrimary
    | IDENT                                          # namePrimary
    | '(' expression ')'                             # parenPrimary
    | collectionKind '{' (expression (',' expression)*)? '}'  # collectionLiteral
    ;

collectionKind : 'Sequence' | 'Set' | 'Bag' | 'OrderedSet' ;

literal
    : INT       # intLiteral
    | REAL      # realLiteral
    | STRING    # stringLiteral
    | 'true'    # trueLiteral
    | 'false'   # falseLiteral
    ;

typeName : IDENT ('::' IDENT)* ;

// ---------------------------------------------------------------------------
// Lexer
// ---------------------------------------------------------------------------

REAL    : [0-9]+ '.' [0-9]+ ;
INT     : [0-9]+ ;
STRING  : '\'' ( ~['\\] | '\\' . )* '\'' ;
IDENT   : [a-zA-Z_] [a-zA-Z_0-9]* ;

WS      : [ \t\r\n]+ -> skip ;
COMMENT : '--' ~[\r\n]* -> skip ;
