using System.Reflection;
using System.Text;
using CommandLine;
using CommandLine.Text;
using Core.Configs;
using Core.Extensions;
using Core.Logic.Builders;
using Core.Logic.Getters;
using Core.Misc;
using Microsoft.Extensions.Logging;

namespace Elib2EbookCli;

internal static class Program {
    private const int EXIT_OK = 0;
    private const int EXIT_BOOK_FAILED = 1;
    private const int EXIT_BAD_INPUT = 2;

    private static async Task<int> Main(string[] args) {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;

        var logger = new ConsoleLogger();

        var parserResult = new Parser(c => {
            c.CaseInsensitiveEnumValues = true;
        }).ParseArguments<Options>(args);

        var exitCode = EXIT_BAD_INPUT;

        await parserResult
            .WithNotParsed(errs => {
                var title = AppDomain.CurrentDomain.FriendlyName;
                var coreAssembly = typeof(BookGetterConfig).Assembly;
                var version = coreAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty;

                var heading = new HeadingInfo(title, version);

                if (errs.IsVersion()) {
                    logger.LogInformation(heading);
                    exitCode = EXIT_OK;
                } else {
                    var helpText = HelpText.AutoBuild(parserResult, h => {
                        h.Heading = heading;
                        h.Copyright = string.Empty;
                        return h;
                    });

                    logger.LogInformation(helpText);
                    exitCode = errs.IsHelp() ? EXIT_OK : EXIT_BAD_INPUT;
                }
            })
            .WithParsedAsync(async options => exitCode = await Run(options, logger));

        return exitCode;
    }

    private static async Task<int> Run(Options options, ILogger logger) {
        var urls = options.Url.Select(u => u.AsUri()).ToList();
        if (urls.Select(u => u.Host).Distinct().Count() > 1) {
            logger.LogInformation("Все переданные ссылки должны быть с одного сайта");
            return EXIT_BAD_INPUT;
        }

        using var getterConfig = BookGetterConfig.GetDefault(options, logger);

        GetterBase getter;
        try {
            getter = GetterProvider.Get(getterConfig, urls.First());
        } catch (ArgumentException ex) {
            logger.LogInformation(ex.Message);
            return EXIT_BAD_INPUT;
        }

        using var _ = getter;

        try {
            await getter.Init();
            await getter.Authorize();
        } catch (Exception ex) {
            logger.LogInformation($"Ошибка авторизации или инициализации. {ex.Message}");
            return EXIT_BAD_INPUT;
        }

        var exitCode = EXIT_OK;
        foreach (var url in urls) {
            logger.LogInformation($"Начинаю генерацию книги {url.ToString().CoverQuotes()}");
            try {
                var book = await getter.Get(url);
                foreach (var format in options.Format) {
                    await BuilderProvider.Get(format, options, logger).Build(book);
                }

                if (options.Additional) {
                    await new AdditionaFileBuilder(options, logger).Build(book);
                }

                if (!options.SaveTemp) {
                    book.Dispose();
                }
            } catch (TaskCanceledException) {
                logger.LogInformation("Сервер не успевает ответить. Попробуйте увеличить Timeout с помощью параметра -t");
                exitCode = EXIT_BOOK_FAILED;
            } catch (Exception ex) {
                logger.LogInformation($"Генерация книги {url} завершилась с ошибкой. {ex}");
                exitCode = EXIT_BOOK_FAILED;
            }
        }

        return exitCode;
    }
}