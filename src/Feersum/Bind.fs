module Bind

open Syntax
open System.Collections.Generic

/// Storage Reference
///
/// Reference to a given storage location. Used to express reads and writes
/// of values to storage locations.
type StorageRef =
    | Builtin of string
    | Local of int
    | Global of string
    | Arg of int
    | Environment of int * StorageRef
    | Captured of StorageRef

/// Collection of Bound Formal Parameters
/// 
/// Different types of formal parameters accepted by lambda definitions.
type BoundFormals =
    | Simple of string
    | List of string list
    | DottedList of string list * string

/// Bound Expression Type
///
/// Bound expressions represent the syntax of a program with all identifier
/// references resolved to the correct storage.
type BoundExpr =
    | Boolean of bool
    | Character of char
    | Number of double
    | Str of string
    | Load of StorageRef
    | Store of StorageRef * BoundExpr option
    | Application of BoundExpr * BoundExpr list
    | If of BoundExpr * BoundExpr * BoundExpr option
    | Seq of BoundExpr list
    | Lambda of BoundFormals * int * StorageRef list * int option * BoundExpr
    | Null

/// Binder Context Type
///
/// Used to pass the state around during the bind. Holds on to scope information
/// and other transient information about the current `bind` call.
type private BinderCtx =
    { Scope: IDictionary<string, StorageRef>
    ; mutable NextEnvSlot: int
    ; mutable Captures: StorageRef list
    ; mutable EnvSize: int option
    ; Storage: string -> StorageRef
    ; Parent: BinderCtx option }

/// Add another element into our environment.
let private incEnvSize = function
    | None -> Some(1)
    | Some(size) -> Some(size + 1)

/// Mark the environment as used, even if nothing is stored in it.
let private markEnvUsed = function
    | None -> Some(0)
    | o -> o

/// Methods for manipulating the bind context
module private BinderCtx =

    /// Create a new binder context for the given root scope
    let createForGlobalScope scope =
        { Scope = new Dictionary<string, StorageRef>(Map.toSeq scope |> dict)
        ; NextEnvSlot = 0
        ; Captures = []
        ; EnvSize = None
        ; Storage = StorageRef.Global
        ; Parent = None }
    
    /// Create a new binder context for a child scope
    let createWithParent parent storageFact =
        { Scope = new Dictionary<string, StorageRef>()
        ; NextEnvSlot = 0
        ; Captures = []
        ; EnvSize = None
        ; Storage = storageFact
        ; Parent = Some(parent) }
    
    let private getNextEnvSlot ctx =
        let next = ctx.NextEnvSlot
        ctx.NextEnvSlot <- next + 1
        next

    let rec private parentLookup ctx id =
        match ctx.Parent with
        | Some(parent) ->
            match lookupAndCapture parent id with
            | Some(outer) -> 
                match outer with
                | Captured(_)
                | Environment(_) -> 
                    ctx.Captures <- outer::ctx.Captures
                    ctx.EnvSize <- markEnvUsed ctx.EnvSize
                    Some(StorageRef.Captured(outer))
                | _ -> Some(outer)
            | None -> None
        | None -> None
    and private lookupAndCapture ctx id =
        match ctx.Scope.TryGetValue(id) with
        | (true, value) ->
            match value with
            | Local(_)
            | Arg(_) ->  
                let envSlot = getNextEnvSlot ctx
                let captured = StorageRef.Environment(envSlot, value)
                ctx.Scope.[id] <- captured
                ctx.EnvSize <- incEnvSize ctx.EnvSize
                Some(captured)
            | _ -> Some(value)
        | _ -> parentLookup ctx id
    

    /// Lookup a given ID in the binder scope
    let tryFindBinding ctx id =
        match ctx.Scope.TryGetValue(id) with
        | (true, value) -> Some(value)
        | _ -> parentLookup ctx id
    
    /// Introduce a binding for the given formal argument
    let addArgumentBinding ctx id idx =
        ctx.Scope.[id] <- StorageRef.Arg idx

    /// Add a new entry to the current scope
    let addBinding ctx id =
        let storage = ctx.Storage(id)
        ctx.Scope.[id] <- storage
        storage

/// Bind an Reference to a Symbol
///
/// Lookup the identifier in the given scope and return the storage that it is
/// currently bound to. 
let private bindIdent ctx id =
    match BinderCtx.tryFindBinding ctx id with
    | Some storage -> storage
    | None -> failwithf "Reference to undefined symbol `%s`" id

/// Bind a Formals List Pattern
///
/// This binds the formals list pattern as supported by `(define .. )` forms,
/// or `(lambda)` forms. Its job is to walk the list of nodes from a form and
/// return either a plain or dotted list pattern. The following
/// types of formals patterns are suppoted:
///   * `(<id>, .. )` - to bind each parameter to a unique identifier
///   * `(<id>, .. '.', <id>)` - to bind a fixed set of parameters with an
///     optional list of extra parameters.
let private bindFormalsList formals =
    let f (acc: string list * bool * Option<string>) formal =
        let (formals, seenDot, afterDot) = acc
        if seenDot then
            if afterDot.IsSome then
                failwith "Only expect single ID after dot"
            match formal with
            | AstNode.Ident(id) -> (formals, true, Some(id))
            | _ -> failwith "Expected ID after dot"
        else
            match formal with
            | AstNode.Dot -> (formals, true, None)
            | AstNode.Ident(id) -> (id::formals, false, None)
            | _ -> failwith "Expected ID or dot in formals"
    
    let (fmls, sawDot, dotted) = List.fold f ([], false, None) formals
    let fmls = List.rev fmls
    if sawDot then
        match dotted with
        | Some(d) -> BoundFormals.DottedList(fmls, d)
        | None -> failwith "Saw dot but no ID in formals"
    else
        BoundFormals.List(fmls)
      
/// Bind a Lambda's Formal Arguments
/// 
/// Parses the argument list for a lambda form and returns a `BoundFormals`
/// instance describing the formal parameter pattern. The following
/// types of formals patterns are suppoted:
///   * `<id>` - to bind the whole list to the given identifier
///   * Any of the list patterns supported by `bindFormalsList`
let private bindFormals formals =
    match formals with
    | AstNode.Ident(id) -> BoundFormals.Simple(id)
    | AstNode.Form(formals) -> bindFormalsList formals
    |  _ -> failwith "Unrecognised formal parameter list. Must be an ID or list pattern"

/// Bind a Syntax Node
///
/// Walks the syntax node building up a bound representation. This bound
/// node no longer has direct references to identifiers and instead
/// references storage locations.
let rec private bindInContext ctx node =
    match node with
    | AstNode.Error -> failwithf "Internal compiler error: Unpexected error node!"
    | AstNode.Number n -> BoundExpr.Number n
    | AstNode.Str s -> BoundExpr.Str s
    | AstNode.Boolean b -> BoundExpr.Boolean b
    | AstNode.Character c -> BoundExpr.Character c
    | AstNode.Dot -> failwith "Unexpected dot"
    | AstNode.Seq s -> bindSequence ctx s
    | AstNode.Form f -> bindForm ctx f
    | AstNode.Ident id -> bindIdent ctx id |> BoundExpr.Load
and private bindSequence ctx exprs =
    List.map (bindInContext ctx) exprs
    |> BoundExpr.Seq
and private bindApplication ctx head rest =
    BoundExpr.Application(bindInContext ctx head, List.map (bindInContext ctx) rest)
and private bindLambdaBody ctx formals body =
    let mutable nextLocal = 0
    let createLocal x =
        let local = nextLocal
        nextLocal <- nextLocal + 1
        StorageRef.Local(local)
    let lambdaCtx = BinderCtx.createWithParent ctx createLocal
    let addFormal idx id =
        BinderCtx.addArgumentBinding lambdaCtx id idx
        idx + 1

    match formals with
    | BoundFormals.Simple(id) ->
        addFormal 0 id |> ignore
    | BoundFormals.List(fmls) ->
        (List.fold addFormal 0 fmls)|> ignore
    | BoundFormals.DottedList(fmls, dotted) ->
        let nextFormal = (List.fold addFormal 0 fmls)
        addFormal nextFormal dotted |> ignore
    let boundBody = bindSequence lambdaCtx body
    BoundExpr.Lambda(formals, nextLocal, lambdaCtx.Captures, lambdaCtx.EnvSize, boundBody)
and private bindForm ctx form =
    match form with
    | AstNode.Ident("if")::body ->
        let b = bindInContext ctx
        match body with
        | [cond;ifTrue;ifFalse] -> BoundExpr.If((b cond), (b ifTrue), Some(b ifFalse))
        | [cond;ifTrue] -> BoundExpr.If((b cond), (b ifTrue), None)
        | _ -> failwith "Ill-formed 'if' special form"
    | AstNode.Ident("begin")::body ->
        List.map (bindInContext ctx) body |> BoundExpr.Seq
    | AstNode.Ident("define")::body ->
        match body with
        | [AstNode.Ident id] ->
            let storage = BinderCtx.addBinding ctx id
            BoundExpr.Store(storage, None)        
        | [AstNode.Ident id;value] ->
            let value = bindInContext ctx value
            let storage = BinderCtx.addBinding ctx id
            BoundExpr.Store(storage, Some(value))
        | (AstNode.Form (AstNode.Ident id::formals))::body ->
            // Add the binding for this lambda to the scope _before_ lowering
            // the body. This makes recursive calls possible.
            BinderCtx.addBinding ctx id |> ignore
            let lambda = bindLambdaBody ctx (bindFormalsList formals) body
            // Look the storage back up. This is key as the lambda, or one of
            // the lambdas nested inside it, could have captured the lambda
            // and moved it to the environment.
            let storage = (BinderCtx.tryFindBinding ctx id).Value
            BoundExpr.Store(storage, Some(lambda))
        | _ -> failwith "Ill-formed 'define' special form"
    | AstNode.Ident("lambda")::body ->
        match body with
        | formals::body ->
            let boundFormals = bindFormals formals
            bindLambdaBody ctx boundFormals body
        | _ -> failwith "Ill-formed 'lambda' special form"
    | AstNode.Ident("let")::body ->
        failwith "Let bindings not yet implemented"
    | AstNode.Ident("let*")::body ->
        failwith "Let* bindings not yet implemented"
    | AstNode.Ident("letrec")::body ->
        failwith "Letrec bindings nto yet implemented"
    | AstNode.Ident("set!")::body ->
        match body with
        | [AstNode.Ident(id);value] ->
            let value = bindInContext ctx value
            let storage = bindIdent ctx id
            BoundExpr.Store(storage, Some(value))
        | _ -> failwith "Ill-formed 'set!' special form"
    | AstNode.Ident("quote")::body ->
        failwith "Quote expressions not yet implemented"
    | AstNode.Ident("and")::body ->
        failwith "And expressions not yet implemented"
    | AstNode.Ident("or")::body ->
        failwith "Or expressions not yet implemented"
    | AstNode.Ident("cond")::body ->
        failwith "Condition expressions not yet implemented"
    | AstNode.Ident("case")::body ->
        failwith "Case expressions not yet implemented"
    | head::rest -> bindApplication ctx head rest
    | [] -> BoundExpr.Null

// ------------------------------ Public Binder API --------------------------

/// Create a New Root Scope
/// 
/// The root scope contains the global functions available to the program.
let createRootScope =
    [ "+"; "-"; "*"; "/"
    ; "="; "<"; ">"; "<="; ">="
    ; "display" ]
    |> Seq.map (fun s -> (s, StorageRef.Builtin(s)))
    |> Map.ofSeq

/// Bind a syntax node in a given scope
/// 
/// Walks the parse tree and computes semantic information. The result of this
/// call can be passed to the `Compile` or `Emit` API to be lowered to IL.
/// 
/// TODO: This should probably return some kind of `BoundSyntaxTree` containing
///       the tree of bound nodes and a bag of diagnostics generated during the
///       bind. Currently we use `failwith` to raise an error. Ideally more than
///       one diagnostic could be reported.
let bind scope node =
    let ctx = BinderCtx.createForGlobalScope scope
    bindInContext ctx node