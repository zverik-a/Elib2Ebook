using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Types.SocialLib;


public class RanobeLibBookDetails {
    [JsonPropertyName("data")]
    public RLBDData Data { get; init; } = null!;

    [JsonPropertyName("meta")]
    public RLBDMeta Meta { get; init; }
}

public class RLBDMeta {
    [JsonPropertyName("country")]
    public string Country { get; init; }
}

public class RLBDData {
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("rus_name")]
    public string RusName { get; init; }

    [JsonPropertyName("eng_name")]
    public string EngName { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; }

    [JsonPropertyName("slug")]
    public string Slug { get; init; }

    [JsonPropertyName("slug_url")]
    public string SlugUrl { get; init; }

    [JsonPropertyName("cover")]
    public RLBDCover Cover { get; init; }

    [JsonPropertyName("background")]
    public RLBDBackground Background { get; init; }

    [JsonPropertyName("age_restriction")]
    public RLBDLabeledItem AgeRestriction { get; init; }

    [JsonPropertyName("site")]
    public int? Site { get; init; }

    [JsonPropertyName("type")]
    public RLBDLabeledItem Type { get; init; }

    [JsonPropertyName("summary")]
    public JsonElement? Summary { get; init; }

    [JsonPropertyName("is_licensed")]
    public bool IsLicensed { get; init; }

    [JsonPropertyName("teams")]
    public List<RLBDTeam> Teams { get; init; } = [];

    [JsonPropertyName("authors")]
    public List<RLBDAuthor> Authors { get; init; } = [];

    [JsonPropertyName("content_marking")]
    public List<RLBDContentMarking> ContentMarking { get; init; } = [];

    [JsonPropertyName("status")]
    public RLBDLabeledItem Status { get; init; }

    [JsonPropertyName("items_count")]
    public RLBDItemsCount ItemsCount { get; init; }

    [JsonPropertyName("releaseDateString")]
    public string ReleaseDateString { get; init; }
}

public class RLBDCover {
    [JsonPropertyName("filename")]
    public string Filename { get; init; }

    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; init; }

    [JsonPropertyName("default")]
    public string Default { get; init; }

    [JsonPropertyName("md")]
    public string Md { get; init; }
}

public class RLBDBackground {
    [JsonPropertyName("filename")]
    public string Filename { get; init; }

    [JsonPropertyName("url")]
    public string Url { get; init; }
}

public class RLBDLabeledItem {
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = null!;
}

public class RLBDTeam {
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("slug")]
    public string Slug { get; init; }

    [JsonPropertyName("slug_url")]
    public string SlugUrl { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("cover")]
    public RLBDCover Cover { get; init; }

    [JsonPropertyName("details")]
    public RLBDTeamDetails Details { get; init; }

    [JsonPropertyName("donate_enabled")]
    public bool DonateEnabled { get; init; }

    [JsonPropertyName("open_chapter_with_content_key_enabled")]
    public bool? OpenChapterWithContentKeyEnabled { get; init; }
}

public class RLBDTeamDetails {
    [JsonPropertyName("branch_id")]
    public int? BranchId { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    [JsonPropertyName("subscriptions_count")]
    public int? SubscriptionsCount { get; init; }
}

public class RLBDAuthor {
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("slug")]
    public string Slug { get; init; }

    [JsonPropertyName("slug_url")]
    public string SlugUrl { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = null!;

    [JsonPropertyName("rus_name")]
    public string RusName { get; init; }

    [JsonPropertyName("alt_name")]
    public string AltName { get; init; }

    [JsonPropertyName("cover")]
    public RLBDCover Cover { get; init; }

    [JsonPropertyName("confirmed")]
    public bool Confirmed { get; init; }

    [JsonPropertyName("user_id")]
    public int UserId { get; init; }
}

public class RLBDContentMarking {
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = null!;

    [JsonPropertyName("formulation")]
    public string Formulation { get; init; }
}

public class RLBDItemsCount {
    [JsonPropertyName("uploaded")]
    public int Uploaded { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public class TipTapDoc {
    [JsonPropertyName("type")]
    public string Type { get; init; } = null!;

    [JsonPropertyName("content")]
    public List<TipTapNode> Content { get; init; } = [];
}

public class TipTapNode {
    [JsonPropertyName("type")]
    public string Type { get; init; } = null!;

    [JsonPropertyName("content")]
    public List<TipTapNode> Content { get; init; } = [];

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("marks")]
    public List<TipTapMark> Marks { get; init; } = [];

    [JsonPropertyName("attrs")]
    public JsonElement? Attrs { get; init; }
}

public class TipTapMark {
    [JsonPropertyName("type")]
    public string Type { get; init; } = null!;

    [JsonPropertyName("attrs")]
    public JsonElement? Attrs { get; init; }
}