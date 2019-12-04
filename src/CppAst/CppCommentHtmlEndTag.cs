using System.Text;

namespace CppAst
{
    /// <summary>
    /// An HTML end comment tag.
    /// </summary>
    public class CppCommentHtmlEndTag : CppCommentHtmlTag
    {
        public CppCommentHtmlEndTag() : base(CppCommentKind.HtmlEndTag)
        {
        }

        protected internal override void ToString(StringBuilder builder)
        {
            builder.Append("</");
            builder.Append(TagName);
            builder.Append(">");
        }
    }
}