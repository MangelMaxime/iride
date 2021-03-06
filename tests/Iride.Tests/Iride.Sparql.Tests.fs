module IrideSparqlTests

open NUnit.Framework
open VDS.RDF
open VDS.RDF.Storage
open Iride
open System

let nodeFactory = NodeFactory()
let literal = nodeFactory.CreateLiteralNode

let storage =
    let inMemoryManager = new InMemoryManager()
    inMemoryManager.Update """INSERT DATA {
        <http://example.org/s> <http://example.org/p> "aa"
    }"""
    inMemoryManager :> IQueryableStorage


type Ask = SparqlCommand<"ASK WHERE {?s ?p $o}">

[<Test>]
let ``Can ask`` () =
    let cmd = Ask(storage)
    cmd.Run(literal "bb") |> Assert.False
    cmd.Run(literal "aa") |> Assert.True

type Construct = SparqlCommand<"CONSTRUCT {?s ?p $o} WHERE {?s ?p $o}">

[<Test>]
let ``Can constuct`` () =
    let cmd = Construct(storage)
    cmd.Run(literal "bb").IsEmpty |> Assert.True
    
    let graph = cmd.Run(literal "aa")
    graph.IsEmpty |> Assert.False
    let triple = graph.Triples |> Seq.exactlyOne
    let s = (triple.Subject :?> IUriNode).Uri
    let p = (triple.Predicate :?> IUriNode).Uri
    let o = (triple.Object :?> ILiteralNode).Value
    Assert.AreEqual(Uri "http://example.org/s", s)
    Assert.AreEqual(Uri "http://example.org/p", p)
    Assert.AreEqual("aa", o)


type Select = SparqlCommand<"SELECT * WHERE {?s ?p $o}">

[<Test>]
let ``Can select`` () =
    let cmd = Select(storage)
    let result = cmd.Run(literal "aa") |> Seq.exactlyOne
    let s = (result.s :?> IUriNode).Uri
    let p = (result.p :?> IUriNode).Uri
    Assert.AreEqual(Uri "http://example.org/s", s)
    Assert.AreEqual(Uri "http://example.org/p", p)

type AskString = SparqlCommand<"ASK WHERE {?s ?p $LIT}">

[<Test>]
let ``Can use typed parameters`` () =
    let cmd = AskString(storage)
    cmd.Run("bb") |> Assert.False
    cmd.Run("aa") |> Assert.True

type SelectString = SparqlCommand<"SELECT * WHERE {?IRI_s ?IRI_p ?LIT}">
[<Test>]
let ``Can use typed results`` () =
    let cmd = SelectString(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    Assert.AreEqual(Uri "http://example.org/s", result.IRI_s)
    Assert.AreEqual(Uri "http://example.org/p", result.IRI_p)
    Assert.AreEqual("aa", result.LIT)

type SelectInt = SparqlCommand<"SELECT * WHERE {?s ?p ?INT}">
[<Test>]
let ``Can use typed int results`` () =
    let storage = new InMemoryManager()
    storage.Update """INSERT DATA {
        <http://example.org/s> <http://example.org/p> 5
    }"""
    let cmd = SelectInt(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    Assert.AreEqual(5, result.INT)
    
type SelectDecimal = SparqlCommand<"SELECT * WHERE {?s ?p ?NUM}">
[<Test>]
let ``Can use typed decimal results`` () =
    let storage = new InMemoryManager()
    storage.Update """INSERT DATA {
        <http://example.org/s> <http://example.org/p> 5.2
    }"""
    let cmd = SelectDecimal(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    Assert.AreEqual(5.2, result.NUM)

type SelectDate = SparqlCommand<"SELECT * WHERE {?s ?p ?DATE}">
[<Test>]
let ``Can use typed date results`` () =
    let storage = new InMemoryManager()
    storage.Update """INSERT DATA {
        <http://example.org/s> <http://example.org/p> "2016-12-01"^^<http://www.w3.org/2001/XMLSchema#date>
    }"""
    let cmd = SelectDate(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    let expected = DateTime(2016, 12, 1)
    Assert.AreEqual(expected, result.DATE)

type SelectTime = SparqlCommand<"SELECT * WHERE {?s ?p ?TIME}">
[<Test>]
let ``Can use typed time results`` () =
    let storage = new InMemoryManager()
    storage.Update """INSERT DATA {
        <http://example.org/s> <http://example.org/p> "2016-12-01T15:31:10-05:00"^^<http://www.w3.org/2001/XMLSchema#dateTime>
    }"""
    let cmd = SelectTime(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    let expected = DateTimeOffset(DateTime(2016, 12, 1, 15, 31, 10), TimeSpan.FromHours -5.)
    Assert.AreEqual(expected, result.TIME)

type SelectOptional = SparqlCommand<"""SELECT * WHERE  
    { { ?s1 ?p1 "aa" } UNION { ?s2 ?p2 "bb" } }  """>
[<Test>]
let ``Can use optional results`` () =
    let cmd = SelectOptional(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    
    Assert.IsTrue(result.s1.IsSome)
    Assert.IsTrue(result.s2.IsNone)

type SelectTypedOptional = SparqlCommand<"""SELECT * WHERE  
    { { ?s1 <http://example.org/p> ?LIT_1 } UNION { ?s2 <http://example.org/q> ?LIT_2} }  """>
[<Test>]
let ``Can use optional typed results`` () =
    let cmd = SelectTypedOptional(storage)
    let result = cmd.Run() |> Seq.exactlyOne
    
    Assert.IsTrue(result.s1.IsSome)
    Assert.IsTrue(result.s2.IsNone)
    Assert.AreEqual(Some "aa", result.LIT_1)
    Assert.AreEqual(None, result.LIT_2)
