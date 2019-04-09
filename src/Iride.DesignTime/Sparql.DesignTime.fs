module SparqlImplementation

open System
open System.Reflection
open FSharp.Core.CompilerServices
open ProviderImplementation.ProvidedTypes
open Iride.SparqlHelper
open Iride
open VDS.RDF.Storage
open VDS.RDF
open VDS.RDF.Query
open Microsoft.FSharp.Quotations

[<TypeProvider>]
type BasicProvider (config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces 
        (config, 
         assemblyReplacementMap = [("Iride.DesignTime", "Iride")],
         addDefaultProbingLocation = true)
    let ns = "Iride"
    let asm = Assembly.GetExecutingAssembly()

    // check we contain a copy of runtime files, and are not referencing the runtime DLL
    do assert (typeof<Iride.QueryRuntime>.Assembly.GetName().Name = asm.GetName().Name)    

    let createType typeName sparqlQuery =
        let result = ProvidedTypeDefinition(asm, ns, typeName, Some typeof<obj>)
        let desc = getQueryDescriptor sparqlQuery
        let parNames = desc.parameters |> List.map (fun x -> x.ParameterName)
        let par = ProvidedParameter("storage", typeof<IQueryableStorage>)
        let ctor = ProvidedConstructor ([par], function 
            | [storage] -> 
                <@@ QueryRuntime(%%storage, sparqlQuery, parNames) :> obj @@>
            | _ -> failwith "Expected a single parameter")
        result.AddMember ctor

        let converter typ =
            if typ = typeof<Uri> then typeof<QueryRuntime>.GetMethod "AsUri"
            elif typ = typeof<string> then typeof<QueryRuntime>.GetMethod "AsString"
            elif typ = typeof<int> then typeof<QueryRuntime>.GetMethod "AsInt"
            elif typ = typeof<decimal> then typeof<QueryRuntime>.GetMethod "AsDecimal"
            elif typ = typeof<DateTimeOffset> then typeof<QueryRuntime>.GetMethod "AsDateTimeOffset"
            else typeof<QueryRuntime>.GetMethod "AsNode"

        let resultType =
            match desc.resultType with
            | ResultType.Boolean -> typeof<bool>
            | ResultType.Graph -> typeof<IGraph>
            | ResultType.Bindings (variables, optionalVariables) ->
                let t = ProvidedTypeDefinition(asm, ns, "Result", Some typeof<obj>)
                let ctorParam = ProvidedParameter("result", typeof<SparqlResult>)
                let ctor = ProvidedConstructor([ctorParam], invokeCode = function
                    | [result] -> <@@  %%result  @@>
                    | _ -> failwith "Expected a single parameter")
                t.AddMember ctor

                variables
                |> List.map (fun v -> ProvidedProperty(v.VariableName, v.Type, getterCode = function
                    | [this] ->
                        let varName = v.VariableName
                        let node = <@@ ((%%this : obj) :?> SparqlResult).Item varName @@>
                        Expr.Call(converter v.Type, [node])
                    | _ -> failwith "Expected a single parameter"))
                |>  List.iter t.AddMember

                optionalVariables
                |> List.map (fun v -> ProvidedProperty(v.VariableName, typeof<INode option>, getterCode = function
                    | [this] ->
                        let varName = v.VariableName
                        <@@ 
                        let r = ((%%this : obj) :?> SparqlResult)
                        if r.HasBoundValue varName then Some (r.Item varName) else None
                        @@>
                    | _ -> failwith "Expected a single parameter"))
                |>  List.iter t.AddMember
                
                result.AddMember t
                t.MakeArrayType()

        let pars =
            desc.parameters 
            |> List.map (fun x -> ProvidedParameter(x.ParameterName, x.Type))
                
        let meth = ProvidedMethod("Run", pars, resultType, invokeCode = function
            | this::pars ->
                let converters = pars |> List.map (fun par ->
                    let m = typeof<QueryRuntime>.GetMethod("ToNode", [| par.Type |])
                    Expr.Call(m, [par]))
                let array = Expr.NewArray(typeof<INode>, converters)
                match desc.resultType with
                | ResultType.Boolean ->
                    <@@ ((%%this: obj) :?> QueryRuntime).Ask(%%array) @@>
                | ResultType.Graph ->
                    <@@ ((%%this: obj) :?> QueryRuntime).Construct(%%array) @@>
                | ResultType.Bindings _ ->
                    <@@ ((%%this: obj) :?> QueryRuntime).Select(%%array) @@>
            | _ -> failwith "unexpected parameters")
        result.AddMember meth
        
        result

    let providerType = 
        let result =
            ProvidedTypeDefinition(asm, ns, "SparqlCommand", Some typeof<obj>)
        let par = ProvidedStaticParameter("SparqlQuery", typeof<string>)
        result.DefineStaticParameters([par], fun typeName args -> 
            createType typeName (string args.[0]))

        result.AddXmlDoc """<summary>TODO.</summary>
           <param name='SparqlQuery'>SPARQL parametrized query.</param>
         """
        result

    do this.AddNamespace(ns, [providerType])