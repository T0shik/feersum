module SyntaxTests

open Xunit
open Syntax

type NodeNoPosition =
    | A of AstNodeKind<NodeNoPosition>
    | B of AstNodeKind<AstNode>

let rec private stripPosition node =
    match node.Kind with
    | Form f -> List.map (stripPosition) f |> Form |> A
    | Seq s -> List.map (stripPosition) s |> Seq |> A
    | _ -> B(node.Kind)

let readSingle input =
    match readExpr input with
    | ({ Kind = Seq exprs }, []) -> (List.exactlyOne exprs).Kind
    | (expr, []) -> expr.Kind
    | (_, diag) -> failwithf "Expected single expression but got: %A" diag

let readMany input =
    match readExpr input with
    | (read, []) -> read  |> stripPosition
    | (_, diag) -> failwithf "Expected one or more expressions but got: %A" diag

// TODO: negative cases for a lot of these parsers. e.g. unterminated strings,
//       invalid hex escapes, bad identifiers and so on.

[<Fact>]
let ``parse seqs`` () =
    Assert.Equal(Seq [ Number 1.0 |> B; Number 23.0 |> B] |> A, readMany "1 23")
    Assert.Equal(Seq [ Boolean true |> B] |> A, readMany "#t")
    Assert.Equal(Seq [ ] |> A, readMany "")
    Assert.Equal(Seq [ Form [ Ident "+" |> B; Number 12.0 |> B; Number 34.0 |> B ] |> A; Boolean false |> B] |> A, readMany "(+ 12 34) #f")

[<Fact>]
let ``parse atoms`` () =
    Assert.Equal(Number 123.559, readSingle "123.559")
    Assert.Equal(Number 789.0, readSingle "789")
    Assert.Equal(Str "hello\nworld", readSingle @"""hello\nworld""")
    Assert.Equal(Str "", readSingle("\"\""))
    Assert.Equal(Ident "nil", readSingle "nil")
    Assert.Equal(Boolean true, readSingle "#t")
    Assert.Equal(Boolean false, readSingle "#f")
    Assert.Equal(Boolean true, readSingle "#true")
    Assert.Equal(Boolean false, readSingle "#false")
    Assert.Equal(Dot, readSingle ".")

[<Theory>]
[<InlineData("?")>]
[<InlineData("+")>]
[<InlineData("*")>]
[<InlineData("/")>]
[<InlineData("-")>]
[<InlineData("a")>]
[<InlineData("test")>]
[<InlineData("test?")>]
[<InlineData("celsius->farenhiet")>]
[<InlineData("things")>]
let ``parse identifiers`` ident =
    Assert.Equal(Ident ident, readSingle ident)

[<Theory>]
[<InlineData("!")>]
[<InlineData("$")>]
[<InlineData("%")>]
[<InlineData("&")>]
[<InlineData("*")>]
[<InlineData("+")>]
[<InlineData("-")>]
[<InlineData("/")>]
[<InlineData(":")>]
[<InlineData("<")>]
[<InlineData("=")>]
[<InlineData(">")>]
[<InlineData("?")>]
[<InlineData("@")>]
[<InlineData("^")>]
[<InlineData("_")>]
[<InlineData("~")>]
[<InlineData("..")>]
[<InlineData("extended.")>]
[<InlineData("extended.identifier")>]
[<InlineData("...")>]
[<InlineData("+soup+")>]
[<InlineData("<=?")>]
[<InlineData("->string")>]
[<InlineData("a34kTMNs")>]
[<InlineData("lambda")>]
[<InlineData("list->vector")>]
[<InlineData("q")>]
[<InlineData("V17a")>]
[<InlineData("the-word-recursion-has-many-meanings")>]
let ``extended identifier characters`` ident =
    Assert.Equal(Ident ident, readSingle ident)

[<Theory>]
[<InlineData("|two words|", "two words")>]
[<InlineData(@"|two\x20;words|", "two words")>]
[<InlineData(@"|\t\t|", "\t\t")>]
[<InlineData(@"|\x9;\x9;|", "\t\t")>]
[<InlineData(@"|H\x65;llo|", "Hello")>]
[<InlineData(@"|\x3BB;|", "λ")>]
let ``identifier literals`` raw cooked =
    Assert.Equal(Ident cooked, readSingle raw)

[<Theory>]
[<InlineData("\\a", '\a')>]
[<InlineData("\\b", '\b')>]
[<InlineData("\\t", '\t')>]
[<InlineData("\\n", '\n')>]
[<InlineData("\\v", '\v')>]
[<InlineData("\\f", '\f')>]
[<InlineData("\\r", '\r')>]
[<InlineData("\\\\", '\\')>]
[<InlineData("\\\"", '"')>]
[<InlineData("\\x0000A;", '\n')>]
[<InlineData("\\x41;", 'A')>]
[<InlineData("\\x1234;", '\u1234')>]
let ``parse escaped characters`` escaped char =
    Assert.Equal(Str (char |> string), readSingle (sprintf "\"%s\"" escaped))

[<Fact>]
let ``parse datum comment`` () =
    Assert.Equal(Number 1.0, readSingle "#;(= n 1)
            1        ;Base case: return 1")
    Assert.Equal(Number 123.0, readSingle "#;(= n 1)123")
    Assert.Equal(Number 456.0, readSingle "#;123 456")

[<Fact>]
let ``parse block comments`` () =
    Assert.Equal(Number 1.0, readSingle "#| this is a comment |#1")
    Assert.Equal(Number 1.0, readSingle "1#| this is a comment |#")
    Assert.Equal(Number 1.0, readSingle "#| this #| is a |# comment |#1")

[<Theory>]
[<InlineData('a')>]
[<InlineData('b')>]
[<InlineData('A')>]
[<InlineData(' ')>]
[<InlineData('#')>]
[<InlineData('\\')>]
[<InlineData('+')>]
[<InlineData('.')>]
[<InlineData('(')>]
[<InlineData('?')>]
[<InlineData('€')>]
[<InlineData('§')>]
[<InlineData('±')>]
let ``parse simple character literals`` char =
    Assert.Equal(Character char, readSingle (@"#\" + string char))

[<Theory>]
[<InlineData("alarm", '\u0007')>]
[<InlineData("backspace", '\u0008')>]
[<InlineData("delete", '\u007F')>]
[<InlineData("escape", '\u001B')>]
[<InlineData("newline", '\u000A')>]
[<InlineData("null", '\u0000')>]
[<InlineData("return", '\u000D')>]
[<InlineData("space", ' ')>]
[<InlineData("tab", '\u0009')>]
let ``parse named characters`` name char =
    Assert.Equal(Character char, readSingle (@"#\" + name))

[<Theory>]
[<InlineData(@"#\x03BB", 'λ')>]
[<InlineData(@"#\x03bb", 'λ')>]
[<InlineData(@"#\x20", ' ')>]
let ``parse hex characters`` hex char =
    Assert.Equal(Character char, readSingle hex)

[<Fact>]
let ``multiple diagnostics on error`` () =
    let source = "(- 1 § (display \"foo\")"
    let (parsed, diagnostics) = readExpr source
    Assert.True(List.length diagnostics > 1)