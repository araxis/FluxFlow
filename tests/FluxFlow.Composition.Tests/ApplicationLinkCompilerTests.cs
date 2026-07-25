using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class ApplicationLinkCompilerTests
{
    [Fact]
    public void Compiler_allows_any_output_payload_to_target_a_signal_port()
    {
        var registry = new CompositionNodeRegistry()
            .Register(
                "source",
                UnusedFactory,
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "signal",
                UnusedFactory,
                inputs: [CompositionPorts.SignalMetadata("Ack")]);
        var definition = Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": { "Type": "source", "Output": "Target.Ack" },
                  "Target": { "Type": "signal" }
                }
              }
            }
            """);

        var result = new ApplicationLinkCompiler(registry).Compile(definition);

        result.IsValid.ShouldBeTrue();
        var link = result.Links.ShouldHaveSingleItem();
        link.Source.ShouldBe(ApplicationAddress.WorkflowPort("Main", "Source", "Output"));
        link.Target.ShouldBe(ApplicationAddress.WorkflowPort("Main", "Target", "Ack"));
        link.MessageType.ShouldBe(typeof(string));
    }

    [Fact]
    public void Compiler_normalizes_mixed_input_and_output_declarations()
    {
        var engine = new TestExpressionEngine();
        var definition = Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Orders": {
                  "Source": {
                    "Type": "source",
                    "Output": [
                      "Primary.Input",
                      { "Port": "Audit.AuditSink.Input", "Condition": "allow" },
                      { "Port": "Secondary.Input", "Condition": "allow" }
                    ]
                  },
                  "Primary": { "Type": "sink" },
                  "Secondary": { "Type": "sink" }
                },
                "Audit": {
                  "AuditSource": { "Type": "source" },
                  "AuditSink": {
                    "Type": "sink",
                    "Input": "AuditSource.Output"
                  }
                }
              }
            }
            """);

        var result = new ApplicationLinkCompiler(CreateRegistry(), engine).Compile(definition);

        result.IsValid.ShouldBeTrue();
        result.Links.Count.ShouldBe(4);
        result.Links.Select(static link => (link.Source.Value, link.Target.Value)).ShouldBe(
        [
            ("Audit.AuditSource.Output", "Audit.AuditSink.Input"),
            ("Orders.Source.Output", "Audit.AuditSink.Input"),
            ("Orders.Source.Output", "Orders.Primary.Input"),
            ("Orders.Source.Output", "Orders.Secondary.Input")
        ]);

        var inputDeclared = result.Links.Single(link =>
            link.Source.Value == "Audit.AuditSource.Output");
        inputDeclared.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Input);
        inputDeclared.IsConditional.ShouldBeFalse();

        result.Links.Count(static link => link.IsConditional).ShouldBe(2);
        var conditional = result.Links.Single(link =>
            link.Target.Value == "Audit.AuditSink.Input" && link.IsConditional);
        conditional.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Output);
        conditional.ConditionExpression.ShouldBe("allow");
        conditional.MessageType.ShouldBe(typeof(string));
        conditional.IsMatch(new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["allow"] = true }
        }).ShouldBeTrue();
        conditional.IsMatch(new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["allow"] = false }
        }).ShouldBeFalse();
        engine.CompileCounts["allow"].ShouldBe(1);
    }

    [Fact]
    public void Compiler_compiles_each_condition_text_once_and_reports_each_invalid_use()
    {
        var engine = new TestExpressionEngine();
        var definition = Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": {
                    "Type": "source",
                    "Output": [
                      { "Port": "First.Input", "Condition": "invalid" },
                      { "Port": "Missing.Input", "Condition": "invalid" }
                    ]
                  },
                  "First": { "Type": "sink" }
                }
              }
            }
            """);

        var result = new ApplicationLinkCompiler(CreateRegistry(), engine).Compile(definition);

        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Diagnostics.Count(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.InvalidCondition).ShouldBe(2);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.MissingComponent);
        engine.CompileCounts["invalid"].ShouldBe(1);
    }

    [Fact]
    public void Component_events_support_normal_addresses_and_conditional_links()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry(), new TestExpressionEngine())
            .Compile(Parse(
                """
                {
                  "Resources": {},
                  "Workflows": {
                    "Orders": {
                      "Source": {
                        "Type": "source",
                        "Events": {
                          "Port": "EventSink.Input",
                          "Condition": "allow"
                        }
                      },
                      "EventSink": { "Type": "event-sink" }
                    }
                  }
                }
                """));

        result.IsValid.ShouldBeTrue();
        var link = result.Links.ShouldHaveSingleItem();
        link.Source.Value.ShouldBe("Orders.Source.Events");
        link.Target.Value.ShouldBe("Orders.EventSink.Input");
        link.MessageType.ShouldBe(typeof(CompositionComponentEvent));
        link.IsConditional.ShouldBeTrue();
        link.IsMatch(new FlowMapContext
        {
            Variables = new Dictionary<string, object?> { ["allow"] = true }
        }).ShouldBeTrue();
    }

    [Fact]
    public void Conditional_links_require_an_expression_engine()
    {
        var definition = Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": {
                    "Type": "source",
                    "Output": { "Port": "Sink.Input", "Condition": "allow" }
                  },
                  "Sink": { "Type": "sink" }
                }
              }
            }
            """);

        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(definition);

        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Diagnostics.Single().Code.ShouldBe(ApplicationLinkDiagnosticCode.MissingConditionEngine);
    }

    [Fact]
    public void Condition_evaluation_failure_is_reported_per_link_without_affecting_siblings()
    {
        var definition = Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": {
                    "Type": "source",
                    "Output": [
                      { "Port": "Failing.Input", "Condition": "fail" },
                      "Healthy.Input"
                    ]
                  },
                  "Failing": { "Type": "sink" },
                  "Healthy": { "Type": "sink" }
                }
              }
            }
            """);
        var result = new ApplicationLinkCompiler(CreateRegistry(), new TestExpressionEngine())
            .Compile(definition);

        result.IsValid.ShouldBeTrue();
        var failing = result.Links.Single(static link => link.IsConditional);
        var healthy = result.Links.Single(static link => !link.IsConditional);

        failing.TryMatch(new FlowMapContext(), out var failure).ShouldBeFalse();
        failure.ShouldBeOfType<InvalidOperationException>();
        healthy.TryMatch(new FlowMapContext(), out var healthyFailure).ShouldBeTrue();
        healthyFailure.ShouldBeNull();
    }

    [Fact]
    public void Conditional_links_express_named_and_default_routes()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry(), new TestExpressionEngine())
            .Compile(Parse(
                """
                {
                  "Resources": {},
                  "Workflows": {
                    "Main": {
                      "Source": {
                        "Type": "source",
                        "Output": [
                          { "Port": "Priority.Input", "Condition": "priority" },
                          { "Port": "Standard.Input", "Condition": "standard" },
                          { "Port": "Fallback.Input", "Condition": "neither-route" }
                        ]
                      },
                      "Priority": { "Type": "sink" },
                      "Standard": { "Type": "sink" },
                      "Fallback": { "Type": "sink" }
                    }
                  }
                }
                """));

        result.IsValid.ShouldBeTrue();
        var priority = result.Links.Single(link => link.Target.Value == "Main.Priority.Input");
        var standard = result.Links.Single(link => link.Target.Value == "Main.Standard.Input");
        var fallback = result.Links.Single(link => link.Target.Value == "Main.Fallback.Input");

        var priorityContext = Context(("priority", true), ("standard", false));
        priority.IsMatch(priorityContext).ShouldBeTrue();
        standard.IsMatch(priorityContext).ShouldBeFalse();
        fallback.IsMatch(priorityContext).ShouldBeFalse();

        var standardContext = Context(("priority", false), ("standard", true));
        priority.IsMatch(standardContext).ShouldBeFalse();
        standard.IsMatch(standardContext).ShouldBeTrue();
        fallback.IsMatch(standardContext).ShouldBeFalse();

        var fallbackContext = Context(("priority", false), ("standard", false));
        priority.IsMatch(fallbackContext).ShouldBeFalse();
        standard.IsMatch(fallbackContext).ShouldBeFalse();
        fallback.IsMatch(fallbackContext).ShouldBeTrue();
    }

    [Fact]
    public void Conditional_links_express_complementary_branches()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry(), new TestExpressionEngine())
            .Compile(Parse(
                """
                {
                  "Resources": {},
                  "Workflows": {
                    "Main": {
                      "Source": {
                        "Type": "source",
                        "Output": [
                          { "Port": "Priority.Input", "Condition": "priority" },
                          { "Port": "Standard.Input", "Condition": "not-priority" }
                        ]
                      },
                      "Priority": { "Type": "sink" },
                      "Standard": { "Type": "sink" }
                    }
                  }
                }
                """));

        result.IsValid.ShouldBeTrue();
        var priority = result.Links.Single(link => link.Target.Value == "Main.Priority.Input");
        var standard = result.Links.Single(link => link.Target.Value == "Main.Standard.Input");

        priority.IsMatch(Context(("priority", true))).ShouldBeTrue();
        standard.IsMatch(Context(("priority", true))).ShouldBeFalse();
        priority.IsMatch(Context(("priority", false))).ShouldBeFalse();
        standard.IsMatch(Context(("priority", false))).ShouldBeTrue();
    }

    [Fact]
    public void Multiple_upstreams_and_fanout_are_allowed_by_default()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "First": {
                    "Type": "source",
                    "Output": ["Sink.Input", "Audit.Input"]
                  },
                  "Second": {
                    "Type": "source",
                    "Output": "Sink.Input"
                  },
                  "Sink": { "Type": "sink" },
                  "Audit": { "Type": "sink" }
                }
              }
            }
            """));

        result.IsValid.ShouldBeTrue();
        result.Links.Count.ShouldBe(3);
    }

    [Fact]
    public void Empty_link_arrays_and_non_port_settings_do_not_create_links()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": {
                    "Type": "source",
                    "Output": [],
                    "BatchSize": 10
                  },
                  "Sink": {
                    "Type": "sink",
                    "Input": []
                  }
                }
              }
            }
            """));

        result.IsValid.ShouldBeTrue();
        result.Links.ShouldBeEmpty();
    }

    [Fact]
    public void Single_link_cardinality_rejects_output_and_input_claims()
    {
        var registry = CreateRegistry(
            sourceCardinality: CompositionPortLinkCardinality.Single,
            sinkCardinality: CompositionPortLinkCardinality.Single);
        var result = new ApplicationLinkCompiler(registry).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "First": {
                    "Type": "source",
                    "Output": ["Shared.Input", "Other.Input"]
                  },
                  "Second": {
                    "Type": "source",
                    "Output": "Shared.Input"
                  },
                  "Shared": { "Type": "sink" },
                  "Other": { "Type": "sink" }
                }
              }
            }
            """));

        result.IsValid.ShouldBeFalse();
        result.Diagnostics.Count(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.ExclusivePortClaim).ShouldBe(2);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Message.Contains("Main.First.Output", StringComparison.Ordinal));
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Message.Contains("Main.Shared.Input", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_links_are_rejected_on_the_same_or_opposite_declaration_side()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "FirstSource": {
                    "Type": "source",
                    "Output": ["FirstSink.Input", "FirstSink.Input"]
                  },
                  "FirstSink": { "Type": "sink" },
                  "SecondSource": {
                    "Type": "source",
                    "Output": "SecondSink.Input"
                  },
                  "SecondSink": {
                    "Type": "sink",
                    "Input": "SecondSource.Output"
                  }
                }
              }
            }
            """));

        result.IsValid.ShouldBeFalse();
        result.Links.Count.ShouldBe(2);
        result.Diagnostics.Count(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.DuplicateLink).ShouldBe(2);
    }

    [Fact]
    public void Endpoint_and_type_failures_are_reported_together()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Unknown": { "Type": "missing.type" },
                  "MissingComponentSource": {
                    "Type": "source",
                    "Output": "Missing.Input"
                  },
                  "IntSource": {
                    "Type": "int-source",
                    "Output": "StringSink.Input"
                  },
                  "MissingInputSource": {
                    "Type": "source",
                    "Output": "StringSink.Missing"
                  },
                  "MissingOutputSink": {
                    "Type": "sink",
                    "Input": "MissingInputSource.Missing"
                  },
                  "SystemTargetSource": {
                    "Type": "source",
                    "Output": "System.Events.Output"
                  },
                  "StringSink": { "Type": "sink" }
                }
              }
            }
            """));

        result.IsValid.ShouldBeFalse();
        var codes = result.Diagnostics.Select(static diagnostic => diagnostic.Code).ToArray();
        codes.ShouldContain(ApplicationLinkDiagnosticCode.UnknownComponentType);
        codes.ShouldContain(ApplicationLinkDiagnosticCode.MissingComponent);
        codes.ShouldContain(ApplicationLinkDiagnosticCode.PortTypeMismatch);
        codes.ShouldContain(ApplicationLinkDiagnosticCode.MissingInputPort);
        codes.ShouldContain(ApplicationLinkDiagnosticCode.MissingOutputPort);
        codes.ShouldContain(ApplicationLinkDiagnosticCode.InvalidPortReference);
    }

    [Fact]
    public void Link_objects_are_strict_and_case_sensitive()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": {
                    "Type": "source",
                    "Output": [
                      42,
                      { "port": "Sink.Input" },
                      { "Port": "" },
                      { "Port": "Sink.Input", "Condition": false },
                      ["Sink.Input"]
                    ]
                  },
                  "Sink": { "Type": "sink" }
                }
              }
            }
            """));

        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Diagnostics.ShouldAllBe(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.InvalidLinkDeclaration);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Message.Contains("case-sensitive", StringComparison.Ordinal));
    }

    [Fact]
    public void System_outputs_are_valid_input_side_sources()
    {
        var result = new ApplicationLinkCompiler(
            CreateRegistry(),
            systemOutputs:
            [
                ApplicationSystemOutputMetadata.Create<string>(ApplicationAddress.SystemEvents)
            ]).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "EventSink": {
                    "Type": "sink",
                    "Input": "System.Events.Output"
                  }
                }
              }
            }
            """));

        result.IsValid.ShouldBeTrue();
        var link = result.Links.Single();
        link.Source.ShouldBe(ApplicationAddress.SystemEvents);
        link.Target.Value.ShouldBe("Main.EventSink.Input");
        link.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Input);
    }

    [Fact]
    public void System_outputs_require_metadata_and_use_exact_type_validation()
    {
        var definition = Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "EventSink": {
                    "Type": "sink",
                    "Input": "System.Events.Output"
                  }
                }
              }
            }
            """);

        var missing = new ApplicationLinkCompiler(CreateRegistry()).Compile(definition);
        var mismatched = new ApplicationLinkCompiler(
            CreateRegistry(),
            systemOutputs:
            [
                ApplicationSystemOutputMetadata.Create<int>(ApplicationAddress.SystemEvents)
            ]).Compile(definition);

        missing.Diagnostics.Single().Code.ShouldBe(
            ApplicationLinkDiagnosticCode.MissingSystemOutputMetadata);
        mismatched.Diagnostics.Single().Code.ShouldBe(
            ApplicationLinkDiagnosticCode.PortTypeMismatch);
        Should.Throw<ArgumentException>(() =>
            ApplicationSystemOutputMetadata.Create<string>(
                ApplicationAddress.WorkflowPort("Main", "Source", "Output")));
    }

    [Fact]
    public void Component_cycles_and_self_links_are_rejected()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "First": {
                    "Type": "transform",
                    "Output": "Other.Second.Input"
                  },
                  "Self": {
                    "Type": "transform",
                    "Output": "Self.Input"
                  }
                },
                "Other": {
                  "Second": {
                    "Type": "transform",
                    "Output": "Main.First.Input"
                  }
                }
              }
            }
            """));

        result.IsValid.ShouldBeFalse();
        result.Diagnostics.Count(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.CycleDetected).ShouldBe(2);
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == ApplicationLinkDiagnosticCode.CycleDetected &&
            diagnostic.WorkflowName == null);
    }

    [Fact]
    public void Port_property_that_is_both_input_and_output_is_ambiguous()
    {
        var result = new ApplicationLinkCompiler(CreateRegistry()).Compile(Parse(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Duplex": {
                    "Type": "duplex",
                    "Port": "Sink.Input"
                  },
                  "Sink": { "Type": "sink" }
                }
              }
            }
            """));

        result.IsValid.ShouldBeFalse();
        result.Links.ShouldBeEmpty();
        result.Diagnostics.Single().Code.ShouldBe(ApplicationLinkDiagnosticCode.AmbiguousPortProperty);
    }

    private static ApplicationDefinition Parse(string json)
        => ApplicationDefinitionJson.Deserialize(json);

    private static FlowMapContext Context(params (string Name, object? Value)[] variables)
        => new()
        {
            Variables = variables.ToDictionary(
                static variable => variable.Name,
                static variable => variable.Value,
                StringComparer.Ordinal)
        };

    private static CompositionNodeRegistry CreateRegistry(
        CompositionPortLinkCardinality sourceCardinality = CompositionPortLinkCardinality.Multiple,
        CompositionPortLinkCardinality sinkCardinality = CompositionPortLinkCardinality.Multiple)
        => new CompositionNodeRegistry()
            .Register(
                "source",
                UnusedFactory,
                outputs: [CompositionPorts.Metadata<string>("Output", sourceCardinality)])
            .Register(
                "int-source",
                UnusedFactory,
                outputs: [CompositionPorts.Metadata<int>("Output")])
            .Register(
                "sink",
                UnusedFactory,
                inputs: [CompositionPorts.Metadata<string>("Input", sinkCardinality)])
            .Register(
                "event-sink",
                UnusedFactory,
                inputs: [CompositionPorts.Metadata<CompositionComponentEvent>("Input")])
            .Register(
                "transform",
                UnusedFactory,
                inputs: [CompositionPorts.Metadata<string>("Input")],
                outputs: [CompositionPorts.Metadata<string>("Output")])
            .Register(
                "duplex",
                UnusedFactory,
                inputs: [CompositionPorts.Metadata<string>("Port")],
                outputs: [CompositionPorts.Metadata<string>("Port")]);

    private static ValueTask<ComposedNode> UnusedFactory(CompositionNodeFactoryContext _)
        => throw new InvalidOperationException("Link compilation must not activate node factories.");

    private sealed class TestExpressionEngine : IFlowExpressionEngine
    {
        private readonly Dictionary<string, int> _compileCounts = new(StringComparer.Ordinal);

        public string Name => "test";

        public IReadOnlyDictionary<string, int> CompileCounts => _compileCounts;

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("Compiled expressions must be reused.");

        public IFlowCompiledExpression<T> Compile<T>(string expression)
        {
            _compileCounts[expression] = _compileCounts.GetValueOrDefault(expression) + 1;
            if (expression == "invalid")
                throw new FormatException("Invalid test expression.");
            if (typeof(T) != typeof(bool))
                throw new NotSupportedException($"Test engine does not compile '{typeof(T)}'.");

            IFlowCompiledExpression<bool> compiled = new TestCompiledExpression(expression);
            return (IFlowCompiledExpression<T>)compiled;
        }
    }

    private sealed class TestCompiledExpression(string expression) : IFlowCompiledExpression<bool>
    {
        public bool Evaluate(FlowMapContext context)
            => expression switch
            {
                "allow" => context.Variables.TryGetValue("allow", out var value) && value is true,
                "priority" => IsTrue(context, "priority"),
                "not-priority" => !IsTrue(context, "priority"),
                "standard" => IsTrue(context, "standard"),
                "neither-route" => !IsTrue(context, "priority") && !IsTrue(context, "standard"),
                "fail" => throw new InvalidOperationException("Condition evaluation failed."),
                _ => false
            };

        private static bool IsTrue(FlowMapContext context, string name)
            => context.Variables.TryGetValue(name, out var value) && value is true;
    }
}
