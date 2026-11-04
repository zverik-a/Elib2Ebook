using System.Net;
using System.Net.Security;
using Elib2Ebook.DomainServices.Extensions;
using Elib2Ebook.DomainServices.Misc.TempFolder;
using FlareSolverrSharp;
using Microsoft.Extensions.Logging;

namespace Elib2Ebook.DomainServices.Configs;

public class BookGetterConfig : IDisposable
{
    public readonly ILogger Logger;

    /// <summary>
    /// SocketsHttpHandler сам следует редиректам, кроме перехода HTTPS -> HTTP (из соображений безопасности).
    /// Часть сайтов так делает, поэтому такие редиректы обрабатываем вручную. Также выдерживает --delay между запросами.
    /// </summary>
    private class RedirectHandler(HttpMessageHandler innerHandler, ILogger logger) : DelegatingHandler(innerHandler)
    {
        private const int MAX_REDIRECTS = 10;

        public double Delay { get; init; }

        private static bool IsRedirect(HttpResponseMessage response) => response is
        {
            StatusCode: HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect,
            Headers.Location: not null
        };

        private static HttpRequestMessage CreateRedirectRequest(HttpRequestMessage original, HttpResponseMessage response)
        {
            var location = response.Headers.Location!;
            if (!location.IsAbsoluteUri)
            {
                location = new Uri(original.RequestUri!, location);
            }

            // Как в браузерах: 307/308 сохраняют метод и тело, остальные редиректы POST превращают в GET
            var keepMethod = response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect || original.Method == HttpMethod.Get;
            var request = new HttpRequestMessage(keepMethod ? original.Method : HttpMethod.Get, location)
            {
                Version = original.Version,
                VersionPolicy = original.VersionPolicy,
                Content = keepMethod ? original.Content : null,
            };

            foreach (var header in original.Headers)
            {
                // Authorization не передаем на чужой хост
                if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && original.RequestUri?.Host != location.Host)
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                for (var i = 0; i < MAX_REDIRECTS && IsRedirect(response); i++)
                {
                    request = CreateRedirectRequest(request, response);
                    response.Dispose();
                    response = await base.SendAsync(request, cancellationToken);
                }

                return response;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                logger.LogInformation("Сервер не успевает ответить. Попробуйте увеличить Timeout с помощью параметра -t");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                logger.LogInformation(ex, "Сервер не успевает ответить. Попробуйте увеличить Timeout с помощью параметра -t");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex.Message);
                throw;
            }
            finally
            {
                if (Delay > 0)
                {
                    logger.LogInformation($"Жду {Delay} секунд(-ы)");
                    await Task.Delay(TimeSpan.FromSeconds(Delay), cancellationToken);
                }
            }
        }
    }

    public HttpClient Client { get; }

    public Options Options { get; }

    public CookieContainer CookieContainer { get; }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(Options.Login) && !string.IsNullOrWhiteSpace(Options.Password);

    public TempFolder TempFolder { get; }

    public BookGetterConfig(Options options, HttpClient client, CookieContainer cookieContainer, TempFolder tempFolder, ILogger logger)
    {
        Logger = logger;
        Client = client;
        CookieContainer = cookieContainer;
        Options = options;
        TempFolder = tempFolder;
    }

    public void Dispose()
    {
        Client?.Dispose();
        TempFolder?.Dispose();
    }

    public static BookGetterConfig GetDefault(Options options, ILogger logger)
    {
        var cookieContainer = new CookieContainer();
        var client = GetClient(options, cookieContainer, logger);

        return new BookGetterConfig(options, client, cookieContainer, TempFolderFactory.Create(options.TempPath), logger);
    }

    private static HttpClient GetClient(Options options, CookieContainer container, ILogger logger)
    {
        var hasProxy = !string.IsNullOrEmpty(options.Proxy);

        var socketsHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = container,
            Proxy = hasProxy ? new WebProxy(options.Proxy.AsUri()) : null,
            UseProxy = hasProxy,

            // Одно TCP-соединение на хост с мультиплексированием HTTP/2 — меньше шансов попасть под лимиты
            EnableMultipleHttp2Connections = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        if (!options.StrictSsl)
        {
            // У многих сайтов просроченные или самоподписанные сертификаты, поэтому по умолчанию проверка отключена
            socketsHandler.SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            };
        }

        HttpMessageHandler rootHandler = new RedirectHandler(socketsHandler, logger)
        {
            Delay = options.Delay,
        };

        if (!string.IsNullOrWhiteSpace(options.Flare))
        {
            var clearanceHandler = new ClearanceHandler(options.Flare)
            {
                MaxTimeout = options.Timeout * 1000,
                InnerHandler = rootHandler,
            };

            if (hasProxy)
            {
                clearanceHandler.ProxyUrl = options.Proxy;
            }

            rootHandler = clearanceHandler;
        }

        return new HttpClient(rootHandler)
        {
            Timeout = TimeSpan.FromSeconds(options.Timeout),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }
}
