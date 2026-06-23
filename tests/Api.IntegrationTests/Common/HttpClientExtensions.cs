using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Models;

namespace Api.IntegrationTests.Common;

public static class HttpClientExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<(System.Net.HttpStatusCode Status, ApiResponse<T>? Body)> GetApiAsync<T>(
        this HttpClient client, string url, CancellationToken ct = default)
    {
        HttpResponseMessage response = await client.GetAsync(url, ct);
        string content = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, JsonSerializer.Deserialize<ApiResponse<T>>(content, JsonOptions));
    }

    /// <summary>Uploads a PDF as multipart/form-data to a file-upload endpoint.</summary>
    public static async Task<(System.Net.HttpStatusCode Status, ApiResponse<T>? Body)> PostPdfAsync<T>(
        this HttpClient client, string url, byte[] pdf, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdf);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "File", fileName);

        HttpResponseMessage response = await client.PostAsync(url, form, ct);
        string content = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, JsonSerializer.Deserialize<ApiResponse<T>>(content, JsonOptions));
    }
}
