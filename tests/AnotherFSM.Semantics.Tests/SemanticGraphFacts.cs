namespace AnotherFSM.Semantics.Tests;

public sealed class SemanticGraphFacts
{
    private static DocumentAnalysis Doc(string text, string id = "flow.fsm") => FsmAnalysis.Analyze(new(id, 1, text));

    [Fact]
    public void Ordinary_symbols_namespace_and_numeric_branches_navigate_without_guessing_implicit_events()
    {
        const string text = "namespace W { def A(Key) { error->E; branch 3->E; } def B() {} E -> A to B; } namespace W { def E event; }";
        var doc = Doc(text);
        var graph = SemanticGraph.Build(new[] { doc }, (_, _) => null);
        var node = graph.Navigate(doc.Document.Id, text.IndexOf("to B", StringComparison.Ordinal) + 3);
        Assert.Equal(new SourceSpan(text.IndexOf("B()", StringComparison.Ordinal), 1), Assert.Single(node.Locations).Span);
        Assert.Equal("Key", graph.FindNode(doc.Document.Id, text.IndexOf("A to", StringComparison.Ordinal))!.TypeKey);
        Assert.Equal(2, graph.Navigate(doc.Document.Id, text.LastIndexOf("W", StringComparison.Ordinal)).Locations.Count);
        Assert.Equal(new SourceSpan(text.IndexOf("E event", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate(doc.Document.Id, text.IndexOf("->E", StringComparison.Ordinal) + 2).Locations).Span);
        Assert.Equal(2, graph.FindReferences(doc.Document.Id, text.IndexOf("error", StringComparison.Ordinal)).Locations.Count);
        var branch = graph.Navigate(doc.Document.Id, text.IndexOf("error", StringComparison.Ordinal), "branchMetadata");
        Assert.Equal("metadata", branch.Status);
        Assert.Contains("3", branch.Reason);
        var implicitDoc = Doc("def N(K) { 1->Only; 2->Only; }", "implicit.fsm");
        var implicitGraph = SemanticGraph.Build(new[] { implicitDoc }, (_, _) => null);
        Assert.Equal("locations", implicitGraph.Navigate("implicit.fsm", 14).Status);
        Assert.Equal(2, implicitGraph.Navigate("implicit.fsm", 14).Locations.Count);
    }

    [Fact]
    public void Public_interfaces_keep_instance_identity_and_real_template_source_locations()
    {
        const string text = "module M { input N; output Done; def N(Key) {1->Done; 2->Done;} def P(Key) {3->Done;} } use M as Left; use M as Right; def End(K) {} Left.Done -> Right.N; Right.Done -> End;";
        var doc = Doc(text);
        var graph = SemanticGraph.Build(new[] { doc }, (_, _) => null);
        var leftOutput = text.IndexOf("Left.Done", StringComparison.Ordinal) + 5;
        var rightInput = text.IndexOf("Right.N", StringComparison.Ordinal) + 6;
        Assert.Equal(new SourceSpan(text.IndexOf("Done;", StringComparison.Ordinal), 4), Assert.Single(graph.Navigate("flow.fsm", leftOutput).Locations).Span);
        Assert.Equal(new SourceSpan(text.IndexOf("Left;", StringComparison.Ordinal), 4), Assert.Single(graph.Navigate("flow.fsm", leftOutput - 5).Locations).Span);
        Assert.Equal(new SourceSpan(text.IndexOf("M {", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", leftOutput - 5, "moduleDefinition").Locations).Span);
        Assert.Equal(new SourceSpan(text.IndexOf("N;", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", leftOutput - 5, "entry").Locations).Span);
        Assert.Equal("Key", graph.FindNode("flow.fsm", rightInput)!.TypeKey);
        Assert.Equal("flow.fsm", graph.FindNode("flow.fsm", rightInput)!.DocumentId);
        Assert.Null(graph.FindNode("flow.fsm", leftOutput));
        Assert.Null(graph.FindNode("flow.fsm", leftOutput - 5));
        var refs = graph.FindReferences("flow.fsm", leftOutput).Locations;
        Assert.DoesNotContain(refs, l => l.Span.Start == text.IndexOf("Right.Done", StringComparison.Ordinal) + 6);
        var all = graph.FindReferences("flow.fsm", leftOutput, template: true).Locations;
        Assert.Contains(all, l => l.Span.Start == text.IndexOf("Right.Done", StringComparison.Ordinal) + 6);
        Assert.Equal(all.Count, all.Select(l => (l.DocumentId, l.Span)).Distinct().Count());
        Assert.Contains(all, l => l.Instances.Count == 2);
        var publishers = graph.Navigate("flow.fsm", leftOutput, "internal").Locations;
        Assert.Equal(new[] { "N", "P" }, publishers.Select(l => l.Label));
    }

    [Fact]
    public void Imports_follow_resolver_documents_and_alias_navigation_without_merging_independent_roots()
    {
        var main = Doc("import Dep as Alias; use Alias as X; def End(K) {} X.Done -> End; E -> End to X.N;",  "C:/p/main.fsm");
        var dep = Doc("import Leaf; module Dep { input N; output Done; def N(Key) {1->Done;} }", "C:/p/Dep.fsm");
        var leaf = Doc("module Leaf { def N(K) {} }", "C:/p/Leaf.fsm");
        var unrelated = Doc("use Dep as Wrong; def End(K) {} Wrong.Done -> End;", "C:/else/main.fsm");
        DocumentAnalysis? Resolve(string id, string name) => (id, name) switch
        {
            ("C:/p/main.fsm", "Dep") => dep,
            ("C:/p/Dep.fsm", "Leaf") => leaf,
            _ => null
        };
        var graph = SemanticGraph.Build(new[] { main, unrelated, dep, Doc(dep.Document.Text, "c:/P/dep.fsm") }, Resolve);
        Assert.Equal(4, graph.Documents.Count);
        Assert.Equal(new SourceSpan(14, 5), Assert.Single(graph.Navigate(main.Document.Id, 25).Locations).Span);
        Assert.Equal(dep.Document.Id, Assert.Single(graph.Navigate(main.Document.Id, 25, "moduleDefinition").Locations).DocumentId);
        Assert.Equal(dep.Document.Id, graph.FindNodeSource(main.Document.Id, main.Document.Text.IndexOf("X.N", StringComparison.Ordinal) + 2)!.DocumentId);
        var templateRefs = graph.FindReferences(dep.Document.Id, dep.Document.Text.IndexOf("Dep {", StringComparison.Ordinal)).Locations;
        Assert.Contains(templateRefs, l => l.DocumentId == main.Document.Id && l.Span.Start == 25);
        Assert.Equal("unresolved", graph.Navigate(unrelated.Document.Id, unrelated.Document.Text.IndexOf("Done", StringComparison.Ordinal)).Status);
    }

    [Fact]
    public void Branch_source_exposes_the_owner_and_DSL_index_not_CSharp_enum_values()
    {
        const string text = "module M { input N; def N(Key) {none->E; success->E; failed->E; error->E; break->E; cancel->E; branch -9->E;} }";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        foreach (var (keyword, index) in new[] { ("none", 0), ("success", 1), ("failed", 2), ("error", 3), ("break", 4), ("cancel", 5), ("-9", -9) })
        {
            var branch = graph.FindBranchSource("flow.fsm", text.IndexOf(keyword, StringComparison.Ordinal));
            Assert.NotNull(branch);
            Assert.Equal(index, branch.Index);
            Assert.Equal("Key", branch.Owner.Node.TypeKey);
            Assert.Equal("flow.fsm", branch.Owner.DocumentId);
        }
        Assert.Null(graph.FindBranchSource("flow.fsm", text.IndexOf("Key", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("E -> A to X;", "X;", true)]
    [InlineData("E -> A to X.Private;", "Private", true)]
    [InlineData("E -> X to A;", "X to", true)]
    [InlineData("E -> X.N to A;", "N to", true)]
    [InlineData("X.Hidden -> A;", "Hidden", true)]
    [InlineData("X.Done -> A;", "Done ->", true)]
    [InlineData("E -> A to X.N;", "N;", false)]
    public void Invalid_connections_never_expose_a_certain_Node_or_reference(string connection, string query, bool invalid)
    {
        const string header = "module M { input N,Q; output Done; def N(K) {1->Hidden; 1->Other;} def Q(K) {} def Private(K) {} } use M as X; def A(K) {} ";
        var doc = Doc(header + connection);
        var graph = SemanticGraph.Build(new[] { doc }, (_, _) => null);
        var offset = header.Length + connection.IndexOf(query, StringComparison.Ordinal);
        Assert.Equal(invalid ? "unresolved" : "resolved", graph.Navigate("flow.fsm", offset).Status);
        if (invalid)
        {
            Assert.Null(graph.FindNodeSource("flow.fsm", offset));
            Assert.DoesNotContain(graph.FindReferences("flow.fsm", header.IndexOf("A(K)", StringComparison.Ordinal)).Locations,
                l => l.Span.Start >= header.Length);
        }
        else Assert.Equal("N", graph.FindNodeSource("flow.fsm", offset)!.Node.Name);
    }

    [Fact]
    public void Namespace_priority_real_instances_and_dotted_ordinary_names_are_not_lexical_member_guesses()
    {
        const string text = "module M { input N; output Done; def N(K) {1->Done;} } use M as X; def X.Other(K) {} def End(K) {} namespace W { use m as x; def End(K) {} X.Done -> End; E -> X.Other to End; } E -> X.Other to End;";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var local = text.IndexOf("X.Done", StringComparison.Ordinal);
        var globalDotted = text.LastIndexOf("X.Other", StringComparison.Ordinal);
        Assert.Equal(new SourceSpan(text.IndexOf("x;", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", local).Locations).Span);
        Assert.Equal(new SourceSpan(text.IndexOf("End(K)", text.IndexOf("namespace", StringComparison.Ordinal), StringComparison.Ordinal), 3), Assert.Single(graph.Navigate("flow.fsm", local + 10).Locations).Span);
        Assert.Equal("X.Other", graph.FindNodeSource("flow.fsm", globalDotted)!.Node.Name);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("X.Other to", StringComparison.Ordinal)).Status);
        Assert.Equal("resolved", graph.Navigate("flow.fsm", local + 2).Status);
    }

    [Fact]
    public void Namespace_local_ordinary_Node_wins_over_a_global_instance_with_the_same_short_name()
    {
        const string text = "module M { input N; def N(K) {} } use M as X; namespace W { def X(K) {} def A(K) {} E -> A to X; }";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var target = text.IndexOf("to X", StringComparison.Ordinal) + 3;
        Assert.Equal("W.X", graph.FindNodeSource("flow.fsm", target)!.Node.Name);
        Assert.Equal(new SourceSpan(text.IndexOf("X(K)", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", target).Locations).Span);
    }

    [Fact]
    public void Module_registration_order_case_rules_and_rejected_legacy_forms_stay_local()
    {
        const string text = "use M as Early; module Original as M { input N; output Done; def N(K) {1->Done;} } use original as Wrong; use m as Good; def A(K) {} E -> A to Good.n; Good.done -> A; E -> A to Good.N;";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("Early", StringComparison.Ordinal)).Status);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("Wrong", StringComparison.Ordinal)).Status);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("Good.n", StringComparison.Ordinal) + 5).Status);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("Good.done", StringComparison.Ordinal) + 5).Status);
        Assert.Equal("N", graph.FindNodeSource("flow.fsm", text.IndexOf("Good.N", StringComparison.Ordinal) + 5)!.Node.Name);
        var nested = "module M { input N; def N(K) {} use M as Nested; } use M as X; def A(K) {} E -> A to X.N;";
        var nestedGraph = SemanticGraph.Build(new[] { Doc(nested) }, (_, _) => null);
        Assert.Null(nestedGraph.FindNodeSource("flow.fsm", nested.IndexOf("N(K)", StringComparison.Ordinal)));
        Assert.Equal("unresolved", nestedGraph.Navigate("flow.fsm", nested.LastIndexOf("X.N", StringComparison.Ordinal) + 2).Status);
        var terminal = Doc("module M { input N; terminal Old; def N(K) {} } use M as X;");
        Assert.Equal("unresolved", SemanticGraph.Build(new[] { terminal }, (_, _) => null).Navigate("flow.fsm", terminal.Document.Text.IndexOf("X;", StringComparison.Ordinal)).Status);
    }

    [Fact]
    public void Missing_cyclic_and_damaged_imports_are_safe_while_trustworthy_local_regions_survive()
    {
        var a = Doc("import B; module A { input N; def N(K) {} }", "C:/p/A.fsm");
        var b = Doc("import A; module B { input N; def N(K) {} }", "C:/p/B.fsm");
        var main = Doc("import A; use A as X; import Missing; def Local(K) {} E -> Local to Local; def Broken(Bad@) {} def Last(K) {}", "C:/p/main.fsm");
        var graph = SemanticGraph.Build(new[] { main }, (_, name) => name == "A" ? a : name == "B" ? b : null);
        Assert.Equal(3, graph.Documents.Count);
        Assert.Equal("unresolved", graph.Navigate(main.Document.Id, 7).Status);
        Assert.Null(graph.FindNodeSource(main.Document.Id, main.Document.Text.IndexOf("Bad@", StringComparison.Ordinal)));
        Assert.Equal("Local", graph.FindNodeSource(main.Document.Id, main.Document.Text.IndexOf("Local to", StringComparison.Ordinal))!.Node.Name);
        Assert.Equal("Last", graph.FindNodeSource(main.Document.Id, main.Document.Text.IndexOf("Last(K)", StringComparison.Ordinal))!.Node.Name);
        var damaged = Doc("module Damaged { input N; def N(K@) {} }", "C:/p/Damaged.fsm");
        var consumer = Doc("import Damaged; use Damaged as X; def A(K) {} E -> A to X.N;");
        var damagedGraph = SemanticGraph.Build(new[] { consumer }, (_, _) => damaged);
        Assert.Equal("unresolved", damagedGraph.Navigate("flow.fsm", consumer.Document.Text.LastIndexOf("X.N", StringComparison.Ordinal) + 2).Status);
    }

    [Fact]
    public void Input_internal_Node_and_external_interface_share_references_but_keep_navigation_roles()
    {
        const string text = "module M { input N; def N(Key) {1->E;} def P(K) {} E -> N to P; } use M as Left; use M as Right; def A(K) {} E -> A to Left.N; F -> A to Right.N;";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var internalUse = text.IndexOf("N to", StringComparison.Ordinal);
        var externalUse = text.IndexOf("Left.N", StringComparison.Ordinal) + 5;
        Assert.Equal(new SourceSpan(text.IndexOf("N(Key)", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", internalUse).Locations).Span);
        Assert.Equal(new SourceSpan(text.IndexOf("N;", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", externalUse).Locations).Span);
        Assert.Equal(new SourceSpan(text.IndexOf("N(Key)", StringComparison.Ordinal), 1), Assert.Single(graph.Navigate("flow.fsm", externalUse, "internal").Locations).Span);
        var all = graph.FindReferences("flow.fsm", internalUse).Locations;
        Assert.Equal(2, Assert.Single(all.Where(l => l.Span.Start == internalUse)).Instances.Count);
        Assert.Single(Assert.Single(all.Where(l => l.Span.Start == externalUse)).Instances);
        var left = graph.FindReferences("flow.fsm", externalUse).Locations;
        Assert.All(left, l => Assert.DoesNotContain(l.Instances, i => i.EndsWith("::Right", StringComparison.Ordinal)));
        Assert.Contains(left, l => l.Span.Start == internalUse);
    }

    [Fact]
    public void A_bad_root_cannot_poison_a_shared_template_in_an_independent_build_scope()
    {
        var template = Doc("module M { input N; output Done; def N(K) {1->Done;} }", "C:/p/M.fsm");
        var good = Doc("import M; use M as X; def A(K) {} E -> A to X.N;", "C:/p/good.fsm");
        var bad = Doc("import M; module M { input Other; def Other(K) {} } use M as X;", "C:/p/bad.fsm");
        var graph = SemanticGraph.Build(new[] { bad, good, template }, (_, _) => template);
        Assert.Equal("N", graph.FindNodeSource(good.Document.Id, good.Document.Text.IndexOf("X.N", StringComparison.Ordinal) + 2)!.Node.Name);
        Assert.Equal("unresolved", graph.Navigate(bad.Document.Id, bad.Document.Text.IndexOf("X;", StringComparison.Ordinal)).Status);
        Assert.Equal("resolved", graph.Navigate(template.Document.Id, template.Document.Text.IndexOf("N(K)", StringComparison.Ordinal)).Status);
    }

    [Fact]
    public void Existing_document_navigation_keeps_module_and_ordinary_Node_scopes_separate()
    {
        const string text = "module M { input N; def N(Key) {} E -> N to N; } module Other { def N(OtherKey) {} E -> N to N; } def N(RootKey) {} E -> N to N;";
        var doc = Doc(text);
        Assert.Equal("Key", doc.FindNodeDeclaration(text.IndexOf("N to", StringComparison.Ordinal))!.TypeKey);
        Assert.Equal("RootKey", doc.FindNodeDeclaration(text.LastIndexOf("N to", StringComparison.Ordinal))!.TypeKey);
        Assert.Equal("OtherKey", doc.FindFactoryDeclaration(text.IndexOf("OtherKey", StringComparison.Ordinal))!.TypeKey);
    }

    [Theory]
    [InlineData("module M { input N; def N(K) {} } use M as X; def x(K) {} def A(K) {} E -> A to X.N;")]
    [InlineData("module M { input N; def N(K) {} } use M as X; def X.N(K) {} def A(K) {} E -> A to X.N;")]
    [InlineData("module M { input N; output Done; def N(K) {1->Done;} } use M as X; def X.Done event; def A(K) {} X.Done -> A;")]
    [InlineData("module M { input N; output B.C; def N(K) {1->B.C;} } module Q { input N; output C; def N(K) {1->C;} } use M as X; use Q as X.B; def A(K) {} X.B.C -> A;")]
    public void Instance_collisions_and_ambiguous_public_owners_do_not_throw_or_pick_a_target(string text)
    {
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var start = text.LastIndexOf("X.", StringComparison.Ordinal);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", start + 2).Status);
        Assert.Null(graph.FindNodeSource("flow.fsm", start + 2));
    }

    [Theory]
    [InlineData("def N(K) {Done->Other;}")]
    [InlineData("def N(K) {[N->Other];}")]
    public void Removed_mapping_and_composition_bodies_never_create_a_certain_Node_binding(string text)
    {
        var doc = Doc(text);
        var graph = SemanticGraph.Build(new[] { doc }, (_, _) => null);
        Assert.Null(graph.FindNodeSource("flow.fsm", text.IndexOf("K)", StringComparison.Ordinal)));
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf("N(K)", StringComparison.Ordinal)).Status);
    }

    [Theory]
    [InlineData("def A(K) {} def B(K) {} E -> A to B; E -> A to B;", "A to")]
    [InlineData("module M { input N; output Done; def N(K) {1->Done;} } use M as X; def B(K) {} X.Done -> B; X.Done -> B;", "Done ->")]
    public void Conflicting_source_Event_connections_are_not_reported_as_certain_references(string text, string query)
    {
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.IndexOf(query, StringComparison.Ordinal)).Status);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", text.LastIndexOf(query, StringComparison.Ordinal)).Status);
    }

    [Fact]
    public void Namespace_prefix_references_cover_Node_Event_and_instance_names_but_not_factory_keys()
    {
        const string text = "namespace W { def A(W.Key) {1->E;} } def W.B(K) {} W.E -> W.A to W.B; module M { input N; def N(K) {} } use M as W.X; F -> W.B to W.X.N;";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var refs = graph.FindReferences("flow.fsm", text.IndexOf("W {", StringComparison.Ordinal)).Locations;
        Assert.Contains(refs, l => l.Span == new SourceSpan(text.IndexOf("W.E ->", StringComparison.Ordinal), 1));
        Assert.Contains(refs, l => l.Span == new SourceSpan(text.IndexOf("W.B(K)", StringComparison.Ordinal), 1));
        Assert.Contains(refs, l => l.Span == new SourceSpan(text.IndexOf("W.X;", StringComparison.Ordinal), 1));
        Assert.DoesNotContain(refs, l => l.Span.Start == text.IndexOf("W.Key", StringComparison.Ordinal));
    }

    [Fact]
    public void Rebuilding_from_versioned_documents_replaces_import_and_instance_associations_without_mutating_old_graphs()
    {
        var root = Doc("import M; use M as X; def A(K) {} E -> A to X.N;", "C:/p/main.fsm");
        var before = FsmAnalysis.Analyze(new("C:/p/M.fsm", 1, "module M { input N; def N(OldKey) {} }"));
        var after = FsmAnalysis.Analyze(new("C:/p/M.fsm", 2, "module M { input N; def N(NewKey) {} }"));
        var oldGraph = SemanticGraph.Build(new[] { root }, (_, _) => before);
        var newGraph = SemanticGraph.Build(new[] { root }, (_, _) => after);
        var offset = root.Document.Text.IndexOf("X.N", StringComparison.Ordinal) + 2;
        Assert.Equal("OldKey", oldGraph.FindNodeSource(root.Document.Id, offset)!.Node.TypeKey);
        Assert.Equal("NewKey", newGraph.FindNodeSource(root.Document.Id, offset)!.Node.TypeKey);
        Assert.Equal(2, Assert.Single(newGraph.Documents.Where(d => d.Document.Id == after.Document.Id)).Document.Version);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => SemanticGraph.Build(new[] { root }, (_, _) => before, cancel.Token));
        Assert.Throws<OperationCanceledException>(() => SemanticGraph.Build(new[] { root }, (_, _) => before,
            checkHostCancellation: () => throw new OperationCanceledException()));
    }

    [Fact]
    public void Imported_plain_scripts_remain_file_backed_templates_without_invented_public_interfaces()
    {
        var main = Doc("import Plain as Alias; use Alias as X; def A(K) {} E -> A to X.N;", "C:/p/main.fsm");
        var plain = Doc("def N(Key) {1->Done;}", "C:/p/Plain.fsm");
        var graph = SemanticGraph.Build(new[] { main }, (_, _) => plain);
        var target = Assert.Single(graph.Navigate(main.Document.Id, 7).Locations);
        Assert.Equal(plain.Document.Id, target.DocumentId);
        Assert.Equal("resolved", graph.Navigate(main.Document.Id, main.Document.Text.IndexOf("X;", StringComparison.Ordinal)).Status);
        Assert.Equal("unresolved", graph.Navigate(main.Document.Id, main.Document.Text.IndexOf("X.N", StringComparison.Ordinal) + 2).Status);
        Assert.Equal("Key", graph.FindNodeSource(plain.Document.Id, 4)!.Node.TypeKey);
    }

    [Fact]
    public void Explicit_Event_declarations_and_Output_are_all_real_declarations_while_Node_identity_is_separate()
    {
        const string text = "module M { input E; output E; def E(K) {1->E;} def E event; } use M as X; def A(K) {} X.E -> A;";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var branchEvent = text.IndexOf("->E", StringComparison.Ordinal) + 2;
        var locations = graph.Navigate("flow.fsm", branchEvent).Locations;
        Assert.Equal(new[] { new SourceSpan(text.IndexOf("E; def", StringComparison.Ordinal), 1), new SourceSpan(text.IndexOf("E event", StringComparison.Ordinal), 1) }, locations.Select(l => l.Span));
        Assert.Single(graph.Navigate("flow.fsm", text.IndexOf("X.E", StringComparison.Ordinal) + 2).Locations);
        Assert.All(graph.FindReferences("flow.fsm", branchEvent).Locations, l => Assert.NotEqual(text.IndexOf("E(K)", StringComparison.Ordinal), l.Span.Start));
        Assert.Equal("K", graph.FindNodeSource("flow.fsm", text.IndexOf("input E", StringComparison.Ordinal) + 6)!.Node.TypeKey);
    }

    [Fact]
    public void Synthetic_module_headers_and_duplicate_Node_branches_cannot_establish_certain_identity()
    {
        const string broken = "module M def N(K) {} } def Last(K) {}";
        var graph = SemanticGraph.Build(new[] { Doc(broken) }, (_, _) => null);
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", 7).Status);
        const string duplicate = "def N(K) {error->E;} def N(K) {branch 3->E;}";
        var duplicates = SemanticGraph.Build(new[] { Doc(duplicate) }, (_, _) => null);
        Assert.Equal("unresolved", duplicates.FindReferences("flow.fsm", duplicate.IndexOf("error", StringComparison.Ordinal)).Status);
        Assert.Null(duplicates.FindBranchSource("flow.fsm", duplicate.IndexOf("error", StringComparison.Ordinal)));
    }

    [Fact]
    public void Namespace_private_expanded_target_cannot_fall_back_to_an_unrelated_global_public_Input()
    {
        const string text = "module Public { input N; def N(K) {} } module Private { input Entry; def Entry(K) {} def N(K) {} } use Public as X; namespace W { use Private as X; def A(K) {} E -> A to X.N; }";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var offset = text.IndexOf("X.N", StringComparison.Ordinal) + 2;
        Assert.Equal("unresolved", graph.Navigate("flow.fsm", offset).Status);
        Assert.Null(graph.FindNodeSource("flow.fsm", offset));
    }

    [Fact]
    public void Module_references_include_import_targets_and_alias_uses_but_never_module_or_alias_declarations()
    {
        var module = Doc("module Checker { input N; output Done; def N(K) {1->Done;} }", "C:/p/Checker.fsm");
        var main = Doc("import Checker as Alias; use Alias as Left; use Alias as Right;", "C:/p/main.fsm");
        var unopened = Doc("import Checker as UnopenedAlias; use UnopenedAlias as Third;", "C:/p/unopened.fsm");
        var graph = SemanticGraph.Build(new[] { main, unopened, module }, (_, _) => module);
        var refs = graph.FindReferences(module.Document.Id, 7);
        Assert.Equal("locations", refs.Status);
        Assert.Equal(new[]
        {
            (main.Document.Id, new SourceSpan(7, 7)),
            (main.Document.Id, new SourceSpan(29, 5)),
            (main.Document.Id, new SourceSpan(48, 5)),
            (unopened.Document.Id, new SourceSpan(7, 7)),
            (unopened.Document.Id, new SourceSpan(37, 13))
        }, refs.Locations.OrderBy(l => l.DocumentId, StringComparer.Ordinal).ThenBy(l => l.Span.Start).Select(l => (l.DocumentId, l.Span)));
        Assert.Equal(new[] { new SourceSpan(29, 5), new SourceSpan(48, 5) }, graph.FindReferences(main.Document.Id, 18).Locations.Select(l => l.Span));
    }

    [Fact]
    public void Node_Event_Input_Output_Instance_and_Namespace_reference_queries_exclude_their_declarations()
    {
        const string text = "module M { input N; output E; def N(K) {1->E;} def P(K) {} E -> N to P; } use M as Left; use M as Right; def A(K) {} Left.E -> A; Right.E -> A; F -> A to Left.N; G -> A to Right.N; def Unused(K) {}";
        var graph = SemanticGraph.Build(new[] { Doc(text) }, (_, _) => null);
        var inputRefs = graph.FindReferences("flow.fsm", text.IndexOf("input N", StringComparison.Ordinal) + 6).Locations;
        Assert.Equal(3, inputRefs.Count);
        Assert.DoesNotContain(inputRefs, l => l.Span.Start == text.IndexOf("N(K)", StringComparison.Ordinal));
        Assert.DoesNotContain(inputRefs, l => l.Span.Start == text.IndexOf("input N", StringComparison.Ordinal) + 6);
        var outputRefs = graph.FindReferences("flow.fsm", text.IndexOf("output E", StringComparison.Ordinal) + 7).Locations;
        Assert.Equal(4, outputRefs.Count);
        Assert.DoesNotContain(outputRefs, l => l.Span.Start == text.IndexOf("output E", StringComparison.Ordinal) + 7);
        Assert.Equal(2, graph.FindReferences("flow.fsm", text.IndexOf("Left;", StringComparison.Ordinal)).Locations.Count);
        Assert.Equal(4, graph.FindReferences("flow.fsm", text.IndexOf("A(K)", StringComparison.Ordinal)).Locations.Count);
        var unused = graph.FindReferences("flow.fsm", text.IndexOf("Unused(K)", StringComparison.Ordinal));
        Assert.Equal("locations", unused.Status);
        Assert.Empty(unused.Locations);
        const string namespaceText = "namespace W { def N(K) {} } E -> W.N to W.N;";
        var namespaces = SemanticGraph.Build(new[] { Doc(namespaceText) }, (_, _) => null).FindReferences("flow.fsm", 10).Locations;
        Assert.Equal(2, namespaces.Count);
        Assert.DoesNotContain(namespaces, l => l.Span.Start == 10);
        const string explicitEvent = "def E event; def N(K) {1->E;} E -> N to N;";
        var events = SemanticGraph.Build(new[] { Doc(explicitEvent) }, (_, _) => null).FindReferences("flow.fsm", 4).Locations;
        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, l => l.Span.Start == 4);
    }

    [Fact]
    public void Module_nodes_expose_real_factory_keys_without_cross_template_name_conflicts()
    {
        var doc = Doc("module A { input N; def N(Key) {} } module B { def N(Key) {} } use A as X; use A as Y;");
        Assert.Equal(2, doc.Nodes.Count);
        Assert.NotEqual(doc.Nodes[0].Scope, doc.Nodes[1].Scope);
        var type = new TypeIdentity("P", "net8.0", "Worker");
        var refs = doc.FindTypeReferences(new[] { new NodeType(type, "Key", true, false) }, type);
        Assert.Equal(new[] { new SourceSpan(26, 3), new SourceSpan(53, 3) }, refs.Select(r => r.Span));
    }
}
