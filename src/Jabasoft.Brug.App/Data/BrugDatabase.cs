using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Jabasoft.Brug.App.Data;

/// <summary>
/// De eigen database van de Brug: JabasoftBrug op LocalDB. Wordt bij het
/// opstarten aangemaakt als hij er nog niet is, en daarna bijgewerkt met
/// de genummerde stappen hieronder (bijgehouden in dbo.Schemaversie).
///
/// Bewust zonder Entity Framework: een paar tabellen met gewone SQL is hier
/// overzichtelijker dan migraties via de tooling, die met een WinUI-project
/// lastig samenwerkt. Elke tabel krijgt CreatedAtUtc en UpdatedAtUtc, als
/// laatste kolommen, die de database zelf vult.
/// </summary>
public static class BrugDatabase
{
    public const string Naam = "JabasoftBrug";
    private const string Server = @"(localdb)\MSSQLLocalDB";

    public static string Verbinding { get; } =
        $"Server={Server};Database={Naam};Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

    private static readonly string Master =
        $"Server={Server};Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";

    /// <summary>Of de database klaar is voor gebruik; false als LocalDB niet bereikbaar was.</summary>
    public static bool Beschikbaar { get; private set; }

    /// <summary>Waarom de database niet beschikbaar is (null als hij dat wel is).</summary>
    public static string? Fout { get; private set; }

    private static readonly string[] Stappen =
    [
        // 1: modellen - waarvoor een model is en waar het goed in is.
        """
        CREATE TABLE dbo.Modellen (
            Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Modellen PRIMARY KEY,
            Naam         NVARCHAR(200)  NOT NULL CONSTRAINT UQ_Modellen_Naam UNIQUE,
            Soort        NVARCHAR(50)   NOT NULL,
            Maker        NVARCHAR(100)  NOT NULL,
            Grootte      NVARCHAR(100)  NOT NULL,
            Doel         NVARCHAR(500)  NOT NULL,
            SterkIn      NVARCHAR(1000) NOT NULL,
            MinderGoedIn NVARCHAR(1000) NOT NULL,
            Notitie      NVARCHAR(2000) NULL,
            CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Modellen_Created DEFAULT SYSUTCDATETIME(),
            UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Modellen_Updated DEFAULT SYSUTCDATETIME())
        """,
    ];

    /// <summary>Maakt de database aan en brengt hem bij. Gooit nooit: lukt het niet, dan is <see cref="Beschikbaar"/> false.</summary>
    public static async Task VoorbereidenAsync()
    {
        try
        {
            await using (var master = new SqlConnection(Master))
            {
                await master.OpenAsync();
                await VoerAsync(master, $"IF DB_ID('{Naam}') IS NULL CREATE DATABASE [{Naam}]");
            }

            await using var verbinding = new SqlConnection(Verbinding);
            await verbinding.OpenAsync();

            await VoerAsync(verbinding, """
                IF OBJECT_ID('dbo.Schemaversie') IS NULL
                CREATE TABLE dbo.Schemaversie (
                    Versie       INT NOT NULL CONSTRAINT PK_Schemaversie PRIMARY KEY,
                    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Schemaversie_Created DEFAULT SYSUTCDATETIME(),
                    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_Schemaversie_Updated DEFAULT SYSUTCDATETIME())
                """);

            await using (var lees = new SqlCommand("SELECT ISNULL(MAX(Versie), 0) FROM dbo.Schemaversie", verbinding))
            {
                var huidig = Convert.ToInt32(await lees.ExecuteScalarAsync());
                for (var stap = huidig; stap < Stappen.Length; stap++)
                {
                    await VoerAsync(verbinding, Stappen[stap]);
                    await VoerAsync(verbinding, $"INSERT INTO dbo.Schemaversie (Versie) VALUES ({stap + 1})");
                    App.AppLog.Schrijf("brug", $"Database {Naam}: stap {stap + 1} uitgevoerd");
                }
            }

            await VulModellenAsync(verbinding);

            Beschikbaar = true;
            Fout = null;
            App.AppLog.Schrijf("brug", $"Database {Naam} klaar");
        }
        catch (Exception fout)
        {
            Beschikbaar = false;
            Fout = fout.Message;
            App.AppLog.Schrijf("brug", $"Database {Naam} niet bereikbaar: {fout.Message}");
        }
    }

    /// <summary>Hoe lang een eenvoudige vraag aan de database duurt; voor Health.</summary>
    public static async Task<(bool Gelukt, TimeSpan Duur, string? Fout)> ControleerAsync()
    {
        var klok = Stopwatch.StartNew();
        try
        {
            await using var verbinding = new SqlConnection(Verbinding);
            await verbinding.OpenAsync();
            await using var opdracht = new SqlCommand("SELECT COUNT(*) FROM dbo.Modellen", verbinding);
            await opdracht.ExecuteScalarAsync();
            return (true, klok.Elapsed, null);
        }
        catch (Exception fout)
        {
            return (false, klok.Elapsed, fout.Message);
        }
    }

    /// <summary>Alle modellen uit de database; zonder database de beginvulling.</summary>
    public static async Task<IReadOnlyList<Modelinfo>> ModellenAsync()
    {
        if (!Beschikbaar)
        {
            return Modelcatalogus.Beginvulling;
        }

        try
        {
            await using var verbinding = new SqlConnection(Verbinding);
            await verbinding.OpenAsync();
            await using var opdracht = new SqlCommand(
                "SELECT Naam, Soort, Maker, Grootte, Doel, SterkIn, MinderGoedIn, Notitie FROM dbo.Modellen ORDER BY Naam",
                verbinding);
            await using var lezer = await opdracht.ExecuteReaderAsync();

            var lijst = new List<Modelinfo>();
            while (await lezer.ReadAsync())
            {
                lijst.Add(new Modelinfo(
                    lezer.GetString(0), lezer.GetString(1), lezer.GetString(2), lezer.GetString(3),
                    lezer.GetString(4), lezer.GetString(5), lezer.GetString(6),
                    lezer.IsDBNull(7) ? null : lezer.GetString(7)));
            }

            return lijst;
        }
        catch (Exception fout)
        {
            App.AppLog.Schrijf("brug", $"Modellen lezen mislukt: {fout.Message}");
            return Modelcatalogus.Beginvulling;
        }
    }

    /// <summary>Bewaart een model (nieuw of aangepast). Null als het gelukt is, anders de reden.</summary>
    public static async Task<string?> BewaarModelAsync(Modelinfo model)
    {
        if (!Beschikbaar)
        {
            return Fout ?? "Database niet beschikbaar";
        }

        try
        {
            await using var verbinding = new SqlConnection(Verbinding);
            await verbinding.OpenAsync();
            await using var opdracht = new SqlCommand("""
                UPDATE dbo.Modellen
                SET Soort = @soort, Maker = @maker, Grootte = @grootte, Doel = @doel,
                    SterkIn = @sterk, MinderGoedIn = @minder, Notitie = @notitie, UpdatedAtUtc = SYSUTCDATETIME()
                WHERE Naam = @naam;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.Modellen (Naam, Soort, Maker, Grootte, Doel, SterkIn, MinderGoedIn, Notitie)
                    VALUES (@naam, @soort, @maker, @grootte, @doel, @sterk, @minder, @notitie);
                """, verbinding);
            VulParameters(opdracht, model);
            await opdracht.ExecuteNonQueryAsync();
            return null;
        }
        catch (Exception fout)
        {
            return fout.Message;
        }
    }

    /// <summary>Zet de beginvulling erin voor modellen die nog niet in de tabel staan.</summary>
    private static async Task VulModellenAsync(SqlConnection verbinding)
    {
        var nieuw = 0;
        foreach (var model in Modelcatalogus.Beginvulling)
        {
            await using var opdracht = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM dbo.Modellen WHERE Naam = @naam)
                    INSERT INTO dbo.Modellen (Naam, Soort, Maker, Grootte, Doel, SterkIn, MinderGoedIn, Notitie)
                    VALUES (@naam, @soort, @maker, @grootte, @doel, @sterk, @minder, @notitie);
                """, verbinding);
            VulParameters(opdracht, model);
            nieuw += await opdracht.ExecuteNonQueryAsync() > 0 ? 1 : 0;
        }

        if (nieuw > 0)
        {
            App.AppLog.Schrijf("brug", $"Modeltabel aangevuld met {nieuw} modellen");
        }
    }

    private static void VulParameters(SqlCommand opdracht, Modelinfo model)
    {
        opdracht.Parameters.AddWithValue("@naam", model.Naam);
        opdracht.Parameters.AddWithValue("@soort", model.Soort);
        opdracht.Parameters.AddWithValue("@maker", model.Maker);
        opdracht.Parameters.AddWithValue("@grootte", model.Grootte);
        opdracht.Parameters.AddWithValue("@doel", model.Doel);
        opdracht.Parameters.AddWithValue("@sterk", model.SterkIn);
        opdracht.Parameters.AddWithValue("@minder", model.MinderGoedIn);
        opdracht.Parameters.AddWithValue("@notitie", (object?)model.Notitie ?? DBNull.Value);
    }

    private static async Task VoerAsync(SqlConnection verbinding, string sql)
    {
        await using var opdracht = new SqlCommand(sql, verbinding);
        await opdracht.ExecuteNonQueryAsync();
    }
}
