using System.Text;
using Core.Configs;
using Core.Extensions;
using Core.Types.SocialLib;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Core.Logic.Getters.LibSocial;

public abstract class MangaLibGetterBase(BookGetterConfig config) : NewLibSocialGetterBase(config) {
    protected override HtmlDocument ResponseToHtmlDoc(SocialLibBookChapter chapterResponse) {
        var sb = new StringBuilder();

        foreach (var page in chapterResponse.Pages) {
            // Страницы лежат на CDN картинок, а не на SystemUrl
            var url = ImagesHost.MakeRelativeUri(page.Url.TrimStart('/'));
            Config.Logger.LogDebug($"Найдена страница в главе: {url}");
            sb.Append($"<img src=\"{url}\" />");
        }

        return sb.AsHtmlDoc();
    }
}