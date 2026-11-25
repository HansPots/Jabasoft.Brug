namespace Jabasoft.Brug.App.Modules.ImageFunn;

/// <summary>Voor welk soort model de prompt bedoeld is.</summary>
public enum Promptsoort
{
    /// <summary>Tekst naar beeld (SDXL/FLUX).</summary>
    Beeld,

    /// <summary>De beweging bij een bestaand beeld (beeld naar video).</summary>
    Beweging,

    /// <summary>Een bestaande prompt aanpassen met een wijzigingsverzoek (bijsturen van een beeld).</summary>
    Bewerk,

    /// <summary>Een hele scène met beweging (tekst naar video).</summary>
    Scene,
}

/// <summary>
/// Maakt van een omschrijving in gewone (Nederlandse) taal een prompt die een
/// beeld- of videomodel begrijpt: die lezen alleen Engels. Loopt via de broker
/// met het chatmodel (zie <see cref="Taalvraag"/>). Overgenomen uit de oude
/// ImageFunn-app (Promptverbeteraar), met dezelfde instructies.
/// </summary>
public static class Promptschrijver
{
    private const string Instructie =
        "You write prompts for a text-to-image model (SDXL/FLUX). The user describes an image in any language. " +
        "Translate it to English and turn it into ONE flowing prompt of at most 70 words. " +
        "Keep EVERY element the user mentions, exactly as asked (subjects, their count, their relative positions " +
        "such as 'in front of' or 'behind'), and never drop or replace one. Put the main subjects first. " +
        "Then add concrete visual detail that fits the request: setting, lighting, mood, colours and composition. " +
        "Keep any style the user mentions (for example watercolour or photo), but do NOT invent a style of your own: the style is added separately. Do not add new subjects. " +
        "Output ONLY the prompt: no quotes, no explanation, no line breaks.";

    private const string BewerkInstructie =
        "You edit a prompt for a text-to-image model. The user gives the ORIGINAL English prompt of an existing image and a " +
        "CHANGE REQUEST in any language (for example: make the person a bit bigger, or add a red bicycle in the background). " +
        "There can be several change requests, one per line: apply ALL of them. " +
        "Return the complete UPDATED English prompt, ONE flowing paragraph of at most 90 words, with the changes applied and " +
        "everything else kept as it was (subjects, style words, lighting, setting). Describe the change concretely: for a " +
        "larger person write that the person fills more of the frame, closer to the camera; for an object name it and say where it is. " +
        "Output ONLY the prompt: no quotes, no explanation, no line breaks.";

    private const string SceneInstructie =
        "You write prompts for a text-to-video model. The user describes, in any language, a short " +
        "video scene. Translate it to English and write ONE flowing paragraph of at most 90 words in present " +
        "tense: start with the main subject and its action, then the setting and background, then lighting, " +
        "colours and mood, then the camera movement (for example: slow push-in, gentle pan, tracking shot, static) " +
        "and the pace. Keep every element the user mentions, keep the motion physically plausible and do not add " +
        "new subjects. Output ONLY the prompt: no quotes, no explanation, no line breaks.";

    private const string BewegingInstructie =
        "You write motion prompts for an image-to-video model. The user describes, in any language, how " +
        "a still image should come to life. Translate it to English and write ONE flowing paragraph of at most 60 " +
        "words in present tense: first what moves and how (subject action, hair, cloth, water, clouds, light), then " +
        "the camera movement (for example: slow push-in, gentle pan, static), then the pace and mood. Keep it " +
        "physically plausible and do not add new subjects or change what is in the picture. " +
        "Output ONLY the prompt: no quotes, no explanation, no line breaks.";

    /// <summary>
    /// Hoeveel woorden een beeldprompt mag zijn voor dit beeldmodel. RealVisXL en Juggernaut (SDXL) lezen maar 77
    /// woorddelen en kappen de rest af; met de stijltekst erachter blijft er voor de omschrijving zo'n 40 woorden over.
    /// FLUX leest er 256, daar mag het ruimer.
    /// </summary>
    public static int MaxWoorden(string beeldmodel, Promptsoort soort) =>
        beeldmodel == "flux" ? (soort == Promptsoort.Bewerk ? 90 : 70) : (soort == Promptsoort.Bewerk ? 45 : 40);

    /// <summary>
    /// De verbeterde prompt, of null met een fout: dan valt de aanroeper terug op de eigen tekst.
    /// <paramref name="beeldmodel"/> bepaalt bij een beeldprompt (Beeld, Bewerk) hoe lang die mag zijn.
    /// </summary>
    public static async Task<(string? Prompt, string? Fout)> VerbeterAsync(string omschrijving, Promptsoort soort = Promptsoort.Beeld, string? beeldmodel = null)
    {
        var instructie = soort switch
        {
            Promptsoort.Beweging => BewegingInstructie,
            Promptsoort.Bewerk => BewerkInstructie,
            Promptsoort.Scene => SceneInstructie,
            _ => Instructie,
        };
        if (beeldmodel is not null && soort is Promptsoort.Beeld or Promptsoort.Bewerk)
        {
            instructie = instructie
                .Replace("at most 70 words", $"at most {MaxWoorden(beeldmodel, soort)} words")
                .Replace("at most 90 words", $"at most {MaxWoorden(beeldmodel, soort)} words");
        }
        var onderdeel = soort switch
        {
            Promptsoort.Beweging => "Beeld naar video (prompt)",
            Promptsoort.Bewerk => "Beeld bijsturen (prompt)",
            Promptsoort.Scene => "Tekst naar video (prompt)",
            _ => "Tekst naar beeld (prompt)",
        };

        var (antwoord, fout, _) = await Taalvraag.VraagAsync(
            "chatModel",
            [new("system", instructie), new("user", omschrijving)],
            onderdeel,
            temperatuur: 0.4,
            limiet: TimeSpan.FromMinutes(3));
        if (antwoord is null)
        {
            return (null, fout);
        }

        var tekst = antwoord.Trim().Trim('"').Replace('\n', ' ').Trim();
        return tekst.Length == 0 ? (null, "Het model gaf een leeg antwoord.") : (tekst, null);
    }
}
