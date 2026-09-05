using System.Text;
using FluxFlow.Components.Http.Materialization;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Http.Tests;

public sealed class HttpClientRequestMaterializerTests
{
    [Fact]
    public void Materialize_accepts_a_structural_request_and_normalizes_json_body()
    {
        var materializer = new HttpClientRequestMaterializer();
        var value = FlowValue.From(new
        {
            method = "POST",
            url = "https://example.test/orders",
            body = new { orderId = 42 }
        });

        var result = materializer.Materialize(value);

        materializer.InputShape.Validate(value).IsValid.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
        result.Value.Method.ShouldBe("POST");
        result.Value.Url.ShouldBe("https://example.test/orders");
        result.Value.Body.ShouldNotBeNull().ContentType.ShouldBe("application/json");
        Encoding.UTF8.GetString(result.Value.Body.Bytes.ToArray()).ShouldBe("{\"orderId\":42}");
    }

    [Fact]
    public void Materialize_rejects_a_non_object_with_http_owned_error()
    {
        var materializer = new HttpClientRequestMaterializer();
        var value = FlowValue.From("invalid");
        var result = materializer.Materialize(value);

        materializer.InputShape.Validate(value).IsValid.ShouldBeFalse();
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull().Code.ShouldBe("http.request.invalid_shape");
        result.Error.Category.ShouldBe("HTTP");
    }
}
