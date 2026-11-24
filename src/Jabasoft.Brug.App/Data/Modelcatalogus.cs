namespace Jabasoft.Brug.App.Data;

/// <summary>
/// Wat de Brug over een model weet: waarvoor het is en waar het goed (en
/// minder goed) in is. Staat in JabasoftBrug.dbo.Modellen en is in het
/// scherm Broker &gt; Modellen aan te passen.
/// </summary>
public sealed record Modelinfo(
    string Naam,
    string Soort,
    string Maker,
    string Grootte,
    string Doel,
    string SterkIn,
    string MinderGoedIn,
    string? Notitie = null);

/// <summary>
/// De beginvulling van de modeltabel: een korte beschrijving per model dat
/// op 24-11-2026 op de AI-server stond. Wordt alleen gebruikt voor modellen
/// die nog niet in de database staan - wat je zelf aanpast, blijft staan.
/// Afgestemd op deze pc: RTX 4070 SUPER met 12 GB VRAM en 32 GB RAM.
/// </summary>
public static class Modelcatalogus
{
    public static IReadOnlyList<Modelinfo> Beginvulling { get; } =
    [
        new("qwen3.6-35b-a3b", "Chat · code", "Alibaba (Qwen)", "35B MoE · ~3B actief",
            "Het werkpaard voor code en agents: Goose, Hermes en codevoorstellen in AI Studio.",
            "Code schrijven en uitleggen, tools aanroepen, lange gesprekken; snel voor zijn grootte doordat maar een klein deel per token rekent.",
            "Past niet helemaal in 12 GB: de experts staan op de CPU, dus laden duurt even en een lange prompt inlezen kost tijd."),
        new("google/gemma-4-12b-qat", "Chat", "Google", "12B · QAT-kwantisatie",
            "Daily chat en controlemodel: het snelle algemene model dat volledig op de GPU past.",
            "Vlot Nederlands, samenvatten, korte vragen, nakijken van een antwoord; laag VRAM-gebruik dankzij QAT.",
            "Minder diep redeneren en minder sterk in grote codetaken dan de 35B."),
        new("google/gemma-4-26b-a4b-qat", "Chat", "Google", "26B MoE · ~4B actief · QAT",
            "Zwaarder alternatief voor Daily chat wanneer een antwoord meer diepgang nodig heeft.",
            "Algemene kennis, schrijven en redeneren op een hoger niveau dan de 12B, toch redelijk snel door MoE.",
            "Groter dan 12 GB: deels op de CPU, dus trager opstarten dan de 12B."),
        new("qwen/qwen3-30b-a3b-2507", "Chat", "Alibaba (Qwen)", "30B MoE · ~3B actief",
            "Snelle generalist zonder denkstap; reserve voor chat en bijtaken.",
            "Direct antwoord, veel kennis, goede instructievolging, ruime context.",
            "Geen aparte denkstap; voor lastige code is de 3.6-35b beter."),
        new("qwen3-30b-a3b-thinking-2507", "Redeneren", "Alibaba (Qwen)", "30B MoE · ~3B actief",
            "Puzzels en rekenwerk waarbij het model eerst hardop nadenkt.",
            "Wiskunde, logica, stap-voor-stap problemen.",
            "Lange denktijd en veel tokens per antwoord; onhandig voor gewone chat. Staat verborgen."),
        new("openai/gpt-oss-20b", "Redeneren", "OpenAI", "21B MoE · ~3,6B actief",
            "Open model van OpenAI voor redeneren en tool-gebruik.",
            "Instelbare denkdiepte, tools aanroepen, Engelstalige technische vragen.",
            "Nederlands minder natuurlijk dan Gemma; vult de 12 GB bijna helemaal."),
        new("qwen2.5-coder-14b-instruct", "Code", "Alibaba (Qwen)", "14B",
            "Klassiek codemodel; reserve als de 3.6-35b te zwaar is.",
            "Code aanvullen en kleine functies schrijven, past goed op de GPU.",
            "Ouder (Qwen 2.5), zwakker in agentwerk en lange opdrachten."),
        new("qwen3-vl-8b-instruct", "Beeld lezen", "Alibaba (Qwen)", "8B · vision-language",
            "Beeldmodel: leest bijgevoegde afbeeldingen voor AI Studio en Hermes.",
            "Beschrijven wat er op een plaatje staat, tekst in schermafbeeldingen lezen, vragen over een foto.",
            "Kleiner taalbrein: voor lange teksten of code een ander model gebruiken."),
        new("text-embedding-bge-m3", "Embedding", "BAAI", "568M · 1024 dimensies",
            "Zoekmodel: maakt de vectoren voor semantisch zoeken in de broker.",
            "Meertalig (ook Nederlands), lange stukken tekst, nauwkeurig terugvinden.",
            "Kan niet chatten; wisselen betekent de zoekindex opnieuw opbouwen."),
        new("text-embedding-nomic-embed-text-v1.5", "Embedding", "Nomic AI", "137M · 768 dimensies",
            "Oud zoekmodel, vervangen door bge-m3.",
            "Klein en snel, goed voor Engelse tekst.",
            "Zwakker in Nederlands. Staat verborgen."),
        new("bytedance/seed-oss-36b", "Redeneren", "ByteDance", "36B",
            "Groot model met zeer lange context (tot 512K tokens).",
            "Hele documenten of codebases in één keer, redeneren met instelbaar denkbudget.",
            "Veel te groot voor 12 GB: grotendeels op de CPU en dus traag. Staat verborgen."),
        new("google/gemma-3-27b", "Chat", "Google", "27B",
            "Voorganger van Gemma 4; algemeen model dat ook plaatjes kan lezen.",
            "Schrijven, vertalen, beeld en tekst samen.",
            "Te groot voor de GPU alleen en ingehaald door Gemma 4. Staat verborgen."),
        new("mistralai/magistral-small-2509", "Redeneren", "Mistral AI", "24B",
            "Redeneermodel van Mistral met zichtbare denkstappen.",
            "Europese talen, gestructureerd redeneren.",
            "Groter dan 12 GB, trager dan de MoE-modellen. Staat verborgen."),
        new("qwen/qwen3-32b", "Chat", "Alibaba (Qwen)", "32B",
            "Dicht 32B-model, sterk maar zwaar.",
            "Kwaliteit van antwoorden, met of zonder denkstap.",
            "Elk token rekent met alle 32B: op deze pc veel trager dan de MoE-varianten. Staat verborgen."),
        new("qwen/qwen3-14b", "Chat", "Alibaba (Qwen)", "14B",
            "Middelgroot Qwen3-model dat volledig op de GPU past.",
            "Snel, met optionele denkstap.",
            "Ingehaald door de 30B/35B MoE-modellen. Staat verborgen."),
        new("qwen2.5-32b-instruct", "Chat", "Alibaba (Qwen)", "32B",
            "Oudere generatie, bewaard ter vergelijking.",
            "Brede kennis en nette instructievolging.",
            "Zwaar en verouderd. Staat verborgen."),
        new("flux.1-schnell", "Afbeelding maken", "Black Forest Labs", "12B · diffusie",
            "Tekst-naar-beeld voor ImageFunn, via de beeldservice (:5330).",
            "Snel (enkele stappen), goede compositie, leest de prompt nauwkeurig, kan tekst in beeld zetten.",
            "Geen chatmodel; vraagt de GPU voor zich alleen (GPU-beurt)."),
        new("juggernaut-xl-ragnarok", "Afbeelding maken", "RunDiffusion (SDXL)", "SDXL · ~3,5B",
            "Fotorealistische beelden in ImageFunn.",
            "Mensen, licht en huid realistisch; veel stijlen.",
            "Minder goed in tekst in beeld; vraagt de GPU voor zich alleen."),
        new("sdxl-models", "Afbeelding maken", "Stability AI", "SDXL",
            "Map met SDXL-basismodellen voor ImageFunn.",
            "Breed inzetbaar, veel uitbreidingen beschikbaar.",
            "Geen los model maar een verzameling; geen chatmodel."),
    ];
}
