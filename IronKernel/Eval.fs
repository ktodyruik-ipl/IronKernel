﻿namespace IronKernel

module Eval =
    
    open Choice
    open Ast
    open Errors
    open System
    open SymbolTable

    let rec continueEval  (Environment(_)) (Continuation (_) as cont) value : ThrowsError<LispVal> = 
        match cont with
        |Continuation {             currentCont = None  ; nextCont = None   } -> returnM value
        |Continuation {closure = e; currentCont = None  ; nextCont = Some nc} -> continueEval e nc value
        |Continuation {closure = e; currentCont = Some (NativeCode{ cont = f ; args = args} ); nextCont = Some nc} -> f e nc value args
        |Continuation {closure = e; currentCont = Some (KernelCode (cBody)); nextCont = Some nc} -> 
            match cBody with
            | [] -> match nc with
                    | Continuation {closure = ne; currentCont = cc; nextCont = nnc} -> continueEval ne (Continuation {closure = ne; currentCont = cc; nextCont = nnc; args = None}) value
                    | _ -> returnM value
            | p::tail -> eval e (Continuation {closure = e; currentCont = Some (KernelCode (tail)); nextCont = Some nc; args = None}) p

    and eval (Environment(_) as env) (Continuation (_) as cont) value : ThrowsError<LispVal> = 
        match value with 
        | Atom(id)          -> either { 
                                let! v = getVar env id 
                                let! r = continueEval env cont v
                                return r
                               }
        | List (op::args)   -> either {    
                                let cps e v a _ =
                                    operate e v a args 
                                let! q = eval env (makeCPS env cont cps) op
                                return q
                               }
        | z -> continueEval env cont z 
    
    and evalArgsEx _env cont args f =  
        let rec cpsEvalArgs e c evaledArg aa = 
            match aa with 
            | Some ([func; List argsEvaled; List argsRemaining]) ->
                match argsRemaining with
                | [] -> operate e c func (argsEvaled@[evaledArg])
                | [a]  -> eval e (makeCPSWArgs e c cpsEvalArgs [func;List(argsEvaled@[evaledArg]);List[]]) a
                | a :: tail -> eval e (makeCPSWArgs e c cpsEvalArgs [func;List(argsEvaled@[evaledArg]);List(tail)]) a
            |_ -> throwError(Default("Internal error at evalArgsEx"))
        let cpsPrepArgs e c func = function 
            | Some args -> match args with
                            | [] -> operate _env cont f []
                            | [a] -> eval _env (makeCPSWArgs e c cpsEvalArgs [f;List[];List[]]) a
                            | a::tail -> eval _env (makeCPSWArgs e c cpsEvalArgs [f;List[];List(tail)]) a
             |_ -> throwError(Default("Internal error at evalArgsEx"))
        eval _env (makeCPSWArgs _env cont cpsPrepArgs args) f
    and evalArgs _env cont args = 
        sequence (List.map (eval _env cont) args) [] 
    and operate (Environment(_) as _env)  (Continuation { currentCont = cc} as cont) (func:LispVal) (args: LispVal list): ThrowsError<LispVal> = 
        
        match func with 
        | PrimitiveOperative f ->  f _env cont args
        | IOFunc f -> either {
                                    let! q = evalArgs _env (newContinuation _env) args
                                    let! r  = f q 

                                    return r
                                  }
        | Applicative f -> evalArgsEx _env cont args f
        | Continuation{ currentCont = cc; nextCont = nc} -> 
                                    match (List.length args) with 
                                    | 0 -> throwError (NumArgs(1,[]))
                                    | 1 -> continueEval _env func (List.head args)
                                    | _ -> continueEval _env (Continuation{ closure = _env; currentCont = cc; nextCont = nc; args = Some (List.tail args)}) (List.head args)
        | Operative { prms = prms ; vararg = vararg; envarg = envarg ; body = body ; closure = closure} -> 
            if List.length prms <> List.length args && vararg  = None then 
                throwError (NumArgs(List.length prms, args))
            else 
                let remainingArgs = List.skip (List.length prms) args
                let evalBody env = 
                    match cont with
                    |Continuation { currentCont = Some (KernelCode cBody); nextCont = Some cCont}
                        -> if List.length cBody = 0 then continueEval env (Continuation { closure = env; currentCont = Some (KernelCode body); nextCont = Some cCont; args = None}) Nil
                            else continueEval env (Continuation { closure = env; currentCont = Some (KernelCode body); nextCont = Some cont; args = None}) Nil
                    | _ ->  continueEval env (Continuation { closure = env; currentCont = Some (KernelCode body); nextCont = Some cont; args = None}) Nil
               
                
                let bindVarArgs arg env = 
                    match arg with
                    |None -> env
                    |Some argName -> bindVars env [ argName, List (remainingArgs)]

                let newEnv = bindVars (newEnv [closure]) (Seq.zip prms args |> Seq.toList) |> bindVarArgs vararg
                
                defineVar newEnv envarg _env |> ignore 
                //printf "Operative: %s\n" (showVal newEnv)
                //printf "*************************\n"
                evalBody newEnv
        | _ when List.length args = 0 -> continueEval _env cont func
        | _ -> throwError (BadSpecialForm("Expecting a function, got ",func))