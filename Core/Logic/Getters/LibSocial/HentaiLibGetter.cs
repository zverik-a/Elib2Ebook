using System;
using Core.Configs;

namespace Core.Logic.Getters.LibSocial;

public class HentaiLibGetter(BookGetterConfig config) : MangaLibGetterBase(config) {
    protected override Uri ImagesHost => new("https://img3h.hentaicdn.org/");

    protected override Uri SystemUrl => new("https://hentailib.me/");

    protected override Uri ApiHost => new("https://hapi.hentaicdn.org/");

    protected override int SiteId => 4;
}