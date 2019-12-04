using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment paragraph.
    /// </summary>
    public class CppCommentParagraph : CppComment
    {
        public CppCommentParagraph() : base(CppCommentKind.Paragraph)
        {
        }

        protected internal override void ToString(StringBuilder builder)
        {
            if (Children != null)
            {
                for (var i = 0; i < Children.Count; i++)
                {
                    var children = Children[i];
                    children.ToString(builder);
                    // If a text is followed by a text, we assume that it was a new line
                    // between the two
                    if (children.Kind == CppCommentKind.Text && i + 1 < Children.Count && Children[i + 1].Kind == CppCommentKind.Text)
                    {
                        var text = ((CppCommentText) children).Text;
                        var nextText = ((CppCommentText) children).Text;
                        if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(nextText))
                        {
                            builder.AppendLine();
                        }
                    }
                }
            }
            builder.AppendLine();
        }
    }
}