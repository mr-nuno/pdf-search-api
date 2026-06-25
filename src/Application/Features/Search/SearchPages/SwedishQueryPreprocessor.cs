namespace Application.Features.Search.SearchPages;

public static class SwedishQueryPreprocessor
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Question words
        "hur", "vad", "vilken", "vilket", "vilka", "varför", "när", "var", "vem",
        // Common question-context verbs
        "fungerar", "fungera", "händer", "gör", "använder", "innebär", "betyder", "sker",
        "ser", "vet", "säger", "kallas",
        // Copula / auxiliaries
        "är", "var", "vara", "vore", "kan", "ska", "bör", "vill", "måste", "får",
        "har", "hade", "bli", "borde", "skulle", "kunde", "ville", "fick",
        // Conjunctions
        "och", "eller", "men", "utan", "fast", "medan", "att", "som", "om",
        // Prepositions / particles
        "i", "på", "av", "med", "för", "till", "från", "vid", "mot", "hos",
        "kring", "bland", "genom", "under", "utan", "efter", "inför", "längs",
        "bredvid", "bakom", "framför", "ovanför", "nedanför", "under", "inom",
        "utanför", "intill", "ovanpå",
        // Articles / determiners
        "en", "ett", "den", "det", "de",
        // Pronouns
        "man", "du", "vi", "han", "hon", "sig", "sin", "sitt", "sina",
        "hans", "hennes", "dess", "deras", "vår", "vårt", "våra",
        "er", "ert", "era", "min", "mitt", "mina", "din", "ditt", "dina",
        // Quantifiers / indefinites
        "alla", "allt", "hela", "varje", "ingen", "inget", "inga",
        "något", "några", "någon", "varandra", "varje",
        // Common adverbs / discourse words
        "inte", "också", "sedan", "nu", "här", "där", "då", "ju", "nog",
        "bara", "mer", "mest", "mindre", "minst", "så", "därför", "dock",
        "ändå", "ofta", "alltid", "aldrig", "sällan", "kanske", "väl",
        "faktiskt", "egentligen", "alltså", "nämligen", "ens", "redan",
        "fortfarande", "igen", "alltmer", "samman",
    };

    /// <summary>
    /// Strips Swedish stop words from the query so that common function words and question
    /// phrases ("hur fungerar X") don't flood search results. Falls back to the original
    /// query unchanged if all tokens are stop words (e.g. a query of just "och").
    /// </summary>
    public static string Process(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var meaningful = tokens.Where(t => !StopWords.Contains(t)).ToArray();
        return meaningful.Length > 0 ? string.Join(' ', meaningful) : query;
    }
}
