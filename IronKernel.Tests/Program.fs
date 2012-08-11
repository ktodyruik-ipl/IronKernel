﻿module IronKernel.Tests

open Xunit
open FsUnit.Xunit

open IronKernel.Ast
open IronKernel.Eval
open IronKernel.Runtime
open IronKernel.Repl
open IronKernel.Choice
open IronKernel.Errors

let eval env s =
    evalString env (newContinuation env) s

let evalSession lines =
    let env = primitiveBindings
    let validate x b =
         either { let! (Bool f) = eqv' [x;b] 
                  return f }
    let test a b = 
        let x = eval env a
        match validate x b with 
        | Choice1Of2(p) -> Assert.True(false,"expecting 'Bool' got '" + showError p + "'")
        | Choice2Of2(y) -> Assert.True(y, "expecting '" + (showVal b) + "' got '" + (showVal x) + "'")

    lines |> List.iter (fun (x,y) -> test x y)

[<Fact>] 
let ``arithmetic 101`` () =
    [
         "(+ 2 2)", Obj 4 ;
         "(+ 2 (* 4 3))" , Obj 14;
         //"(+ 2 (* 4 3) (- 5 7))" , Obj 12;
         "(define x 3)", Inert;
         "(+ x 2)", Obj 5;
         "(eqv? 1 3)", Bool false;
         "(eqv? 3 3)", Bool true;
    ] |> evalSession 

(* *)
[<Fact>] 
let ``continuations`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(call/cc (lambda (k)  (* 5 4)))", Obj 20 ;
        "(call/cc (lambda (k)  (* 5 (k 4))))", Obj 4 ;
        "(let ((x (call/cc (lambda (k) k))))  (x (lambda (_) \"hi\")))", Obj "hi" ;
    ] |> evalSession
    
[<Fact>] 
let ``shift and reset`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(- (reset (+ 3 (* 5 2))) 1)", Obj 12 ;
        "(reset (+ 3 (shift (lambda (k) (* 5 2)))))", Obj 10;
        "(- (reset (+ 3 (shift (lambda (k) (* 5 2))))) 1)", Obj 9 ;
        "(reset (let ((x 1)) (+ 10 (shift (lambda (k) x))))))", Obj 1 ;
        "(+ 1 (reset (+ 2 (shift (lambda (k) 3)))))", Obj 4
        "(cons 1 (reset (cons 2 (shift (lambda (k) (cons 3 '()))))))", List [ Obj 1 ; Obj 3];
        "(cons 1 (reset (cons 2 '())))", List [ Obj 1; Obj 2] ;
        "(reset (+ (shift (lambda (k) (k 7))) 1))", Obj 8;
        "(+ (reset (+ (shift (lambda (k) (k 7))) 1)) 3)", Obj 11;
        "(reset (+ (shift (lambda (k) (+ 2 (k 7)))) 3))", Obj 12;
        "(+ 1 (reset (+ 2 (shift (lambda (k) (+ 3 (k 4)))))))", Obj 10;
        "(cons 'a (reset (cons 'b (shift (lambda (k) (cons 1 (k (k (cons 'c '())))))))))",  List [Atom "a"; Obj 1; Atom "b"; Atom "b"; Atom "c"] ;
        "(cons 1 (reset (cons 2 (shift (lambda (k) (cons 3 (k (cons 4 '()))))))))", List [ Obj 1; Obj 3; Obj 2; Obj 4] ;
        "(+ 1 (reset (+ 2 (shift (lambda (k) (+ 3 (k 5) (k 1)))))))", Obj 14 ;
        "(cons 1 (reset (cons 2 (shift (lambda (k) (cons 3 (k (k (cons 4 '())))))))))", List [ Obj 1; Obj 3; Obj 2; Obj 2; Obj 4] ;
        "(defn (yield x) (shift (lambda (k) (cons x (k (#inert))))))", Inert
        "(reset (begin (yield 1) (yield 2) (yield 3) ()))", List [ Obj 1; Obj 2; Obj 3]
    ] |> evalSession     

[<Fact>] 
let ``lambda, define and map`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(define double (lambda (x) (* 2 x)))", Inert ;
        "(map double (list 1 2 3 4))", List [Obj 2; Obj 4; Obj 6; Obj 8]
    ] |> evalSession 

[<Fact>] 
let ``let`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(let ((x 2) (y 3)) (* x y))", Obj 6 ;
        "(let ((x 2) (y 3)) (let ((x 7) (z (+ x y))) (* z x)))" , Obj 35;
    ] |> evalSession

[<Fact>] 
let ``let*`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(let* ((x 3) (y x)) (+ x y))", Obj 6 ;
        "(let ((x 2) (y 3)) (let* ((x 7) (z (+ x y))) (* z x)))", Obj 70 ;
    ] |> evalSession

[<Fact>] 
let ``letrec`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(letrec ((sum (lambda (x) (if (zero? x) 0 (+ x (sum (- x 1))))))) (sum 5))", Obj 15 ;
    ] |> evalSession

[<Fact>] 
let ``import`` () =
    [
        "(load \"kernel.scm\")", Inert ;
        "(define my-env (bindings->environment (a 1) (b 2) (c 3)))", Inert ;
        "(import! my-env a b)", Inert ;
        "a", Obj 1 ;
        "b", Obj 2 ;
        //"c", Obj 3 ; //how do we signal the ones we expect to fail ?
    ] |> evalSession