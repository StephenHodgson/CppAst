namespace CppAst
{
    /// <summary>
    /// An enumeration for <see cref="CppComment"/>
    /// </summary>
    public enum CppCommentKind
    {
        Null = 0,
        Text = 1,
        InlineCommand = 2,
        HtmlStartTag = 3,
        HtmlEndTag = 4,
        Paragraph = 5,
        BlockCommand = 6,
        ParamCommand = 7,
        TemplateParamCommand = 8,
        VerbatimBlockCommand = 9,
        VerbatimBlockLine = 10,
        VerbatimLine = 11,
        Full = 12
    }
}