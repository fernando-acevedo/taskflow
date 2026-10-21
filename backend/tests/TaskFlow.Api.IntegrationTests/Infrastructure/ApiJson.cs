using System.Net.Http.Json;
using System.Text.Json;

namespace TaskFlow.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Reads API responses strictly: a field missing from the JSON, or a null in a non-nullable one, fails the test
/// instead of quietly becoming <c>false</c>, <c>null</c> or <c>default</c> and satisfying the assertion.
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Options, Ct))!;

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string uri) =>
        (await client.GetFromJsonAsync<T>(uri, Options, Ct))!;
}
