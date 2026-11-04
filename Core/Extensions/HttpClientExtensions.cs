using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Core.Extensions;

public static class HttpClientExtensions {
    private const int MAX_TRY_COUNT = 5;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Ответы, повтор которых бессмысленен: запрос некорректен или ресурса нет.
    /// 403 намеренно повторяем — анти-бот защиты (DDoS-Guard и т.п.) часто отдают его временно.
    /// </summary>
    private static bool IsPermanentFailure(HttpStatusCode code) => code is
        HttpStatusCode.BadRequest or
        HttpStatusCode.Unauthorized or
        HttpStatusCode.NotFound or
        HttpStatusCode.MethodNotAllowed or
        HttpStatusCode.Gone;

    private static bool IsTransientException(Exception ex) => ex is HttpRequestException or TaskCanceledException or IOException;

    /// <summary>
    /// Экспоненциальная задержка с джиттером: base, 2*base, 4*base... но не больше MaxBackoff
    /// </summary>
    internal static TimeSpan GetBackoff(TimeSpan errorTimeout, int attempt) {
        var baseDelay = errorTimeout == default ? DefaultTimeout : errorTimeout;
        var delay = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.NextDouble() * 0.25 * delay;
        return TimeSpan.FromMilliseconds(Math.Min(delay + jitter, MaxBackoff.TotalMilliseconds));
    }

    /// <summary>
    /// Задержка из заголовка Retry-After (секунды или дата), если сервер его прислал
    /// </summary>
    internal static TimeSpan? GetRetryAfter(HttpResponseMessage response) {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter == null) {
            return null;
        }

        var delay = retryAfter.Delta ?? (retryAfter.Date.HasValue ? retryAfter.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (delay == null) {
            return null;
        }

        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > MaxRetryAfter ? MaxRetryAfter : delay;
    }

    /// <summary>
    /// Единая логика повторов. Возвращает первый успешный ответ, либо последний неуспешный
    /// (вызывающий код сам проверяет StatusCode), либо бросает последнее исключение.
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetry(Func<Task<HttpResponseMessage>> send, TimeSpan errorTimeout) {
        for (var attempt = 0; ; attempt++) {
            var isLast = attempt == MAX_TRY_COUNT - 1;

            HttpResponseMessage response;
            try {
                response = await send();
            } catch (Exception ex) when (!isLast && IsTransientException(ex)) {
                await Task.Delay(GetBackoff(errorTimeout, attempt));
                continue;
            }

            if (response.IsSuccessStatusCode || isLast || IsPermanentFailure(response.StatusCode)) {
                return response;
            }

            var delay = response.StatusCode == HttpStatusCode.TooManyRequests ? GetRetryAfter(response) : null;
            response.Dispose();
            await Task.Delay(delay ?? GetBackoff(errorTimeout, attempt));
        }
    }

    public static Task<HttpResponseMessage> GetWithTriesAsync(this HttpClient client, Uri url, TimeSpan errorTimeout = default) {
        return SendWithRetry(() => client.GetAsync(url), errorTimeout);
    }

    public static Task<HttpResponseMessage> SendWithTriesAsync(this HttpClient client, Func<HttpRequestMessage> message, TimeSpan errorTimeout = default) {
        return SendWithRetry(() => client.SendAsync(message()), errorTimeout);
    }

    public static async Task<HttpResponseMessage> PostWithTriesAsync(this HttpClient client, Uri url, HttpContent content, TimeSpan errorTimeout = default) {
        // Буферизуем тело, чтобы его можно было отправить повторно (StreamContent иначе не перечитать)
        await content.LoadIntoBufferAsync();
        return await SendWithRetry(() => client.PostAsync(url, content), errorTimeout);
    }

    public static async Task<HtmlDocument> GetHtmlDocWithTriesAsync(this HttpClient client, Uri url, Encoding encoding = null) {
        using var response = await client.GetWithTriesAsync(url);
        return await response.Content.ReadAsStreamAsync().ContinueWith(t => t.Result.AsHtmlDoc(encoding));
    }

    public static async Task<T> GetFromJsonWithTriesAsync<T>(this HttpClient client, Uri url, TimeSpan errorTimeout = default) {
        using var response = await client.GetWithTriesAsync(url, errorTimeout);
        if (response == default) {
            throw new Exception($"Не удалось получить ответ от {url}");
        }

        if (response.StatusCode != HttpStatusCode.OK) {
            var errorBody = await SafeReadAsStringAsync(response.Content);
            throw new Exception($"Ошибка запроса {url}. Код {(int)response.StatusCode} ({response.StatusCode}).{FormatSnippet(errorBody)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);

        var payload = Encoding.UTF8.GetString(memoryStream.ToArray());
        if (string.IsNullOrWhiteSpace(payload)) {
            throw new Exception($"Ответ {url} пустой");
        }

        if (LooksLikeHtml(payload)) {
            throw new Exception($"Сайт вернул HTML вместо JSON по адресу {url}. Возможно сработала защита (DDoS-Guard). Попробуйте увеличить параметры --delay/--timeout или настроить --flare.");
        }

        try {
            return JsonSerializer.Deserialize<T>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new Exception($"Сайт вернул пустой JSON по адресу {url}");
        } catch (JsonException ex) {
            throw new Exception($"Не удалось разобрать JSON ответа {url}. {ex.Message}. {FormatSnippet(payload)}", ex);
        }
    }

    public static async Task<HtmlDocument> PostHtmlDocWithTriesAsync(this HttpClient client, Uri url, HttpContent content, Encoding encoding = null) {
        using var response = await client.PostWithTriesAsync(url, content);
        return await response.Content.ReadAsStreamAsync().ContinueWith(t => t.Result.AsHtmlDoc(encoding));
    }

    private static async Task<string> SafeReadAsStringAsync(HttpContent content) {
        try {
            return await content.ReadAsStringAsync();
        } catch {
            return string.Empty;
        }
    }

    private static bool LooksLikeHtml(string value) {
        return value.TrimStart().StartsWith("<", StringComparison.Ordinal);
    }

    private static string FormatSnippet(string value, int maxLength = 300) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (LooksLikeHtml(trimmed)) {
            return string.Empty;
        }
        if (trimmed.Length > maxLength) {
            trimmed = trimmed[..maxLength] + "…";
        }

        return $" Ответ сервера: {trimmed}";
    }
}