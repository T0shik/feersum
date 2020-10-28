module Eval

open Syntax
open Bind
open Compile
open System.IO
open System.Reflection
open System
open System.Runtime.ExceptionServices
open Serehfa

/// Raw External Representation
///
/// Returns the external representation for a CIL type.
let cilExternalRepr (object: Object) =
    match object with
    | :? Boolean as b -> if b then "#t" else "#f"
    | :? Double as d -> sprintf "%g" d
    | :? Char as c -> @"#\" + string c
    | null -> "()"
    | :? Func<obj[], obj> as f -> sprintf "#<compiledProcedure %A>" f.Method
    | :? string as s -> sprintf "%A" s
    | other -> Write.GetDisplayRepresentation(other)


/// Take a syntax tree and evaluate it in-process
///
/// This first compiles the tree to an in-memory assembly and then calls the
/// main method on that.
let eval ast =
    let memStream = new MemoryStream()
    let diags = compile memStream "evalCtx" None ast
    if not diags.IsEmpty then
        Result.Error(diags)
    else
        let assm = Assembly.Load(memStream.ToArray())
        let progTy = assm.GetType("evalCtx.LispProgram")
        let mainMethod = progTy.GetMethod("$ScriptBody")
        try
            Ok(mainMethod.Invoke(null, Array.empty<obj>))
        with
        | :? TargetInvocationException as ex  ->
            // Unwrap target invocation exceptions a little to make the REPL a
            // bit of a nicer experience
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            Result.Error([])