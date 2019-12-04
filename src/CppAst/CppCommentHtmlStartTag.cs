using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// An HTML start comment tag.
    /// </summary>
    public class CppCommentHtmlStartTag : CppCommentHtmlTag
    {
        public CppCommentHtmlStartTag() : base(CppCommentKind.HtmlStartTag)
        {
            Attributes = new List<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// Gets or sets a boolean indicating if this start tag is self closing.
        /// </summary>
        public bool IsSelfClosing { get; set; }

        /// <summary>
        /// Gets the list of HTML attributes attached to this start tag.
        /// </summary>
        public List<KeyValuePair<string, string>> Attributes { get; }

        protected internal override void ToString(StringBuilder builder)
        {
            builder.Append("<");
            builder.Append(TagName);

            foreach (var keyValuePair in Attributes)
            {
                builder.Append(" ");
                builder.Append(keyValuePair.Key);
                builder.Append("=\"");
                builder.Append(keyValuePair.Value);
                builder.Append("\"");
            }

            if (IsSelfClosing)
            {
                builder.Append(" /");
            }
            builder.Append(">");
        }
    }
}