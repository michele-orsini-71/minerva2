static class  ContextualText
{
    public static string buildContextualText(string content, string? contextPrefix)
    {
        return contextPrefix is not null
                ? contextPrefix + "\n" + content
                : content;
    }
}