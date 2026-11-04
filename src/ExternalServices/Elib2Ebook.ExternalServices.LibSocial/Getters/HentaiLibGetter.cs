using Elib2Ebook.DomainServices.Configs;

namespace Elib2Ebook.ExternalServices.LibSocial.Getters;

public class HentaiLibGetter(BookGetterConfig config) : MangaLibGetterBase(config)
{
    protected override Uri ImagesHost => new("https://img3h.hentaicdn.org/");

    protected override Uri ApiHost => new("https://hapi.hentaicdn.org/");

    protected override Uri SystemUrl => new("https://hentailib.me/");

    protected override int SiteId => 4;
}
