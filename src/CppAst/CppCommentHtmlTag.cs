using System.Text;

namespace CppAst
{
    /// <summary>
    /// Base class for an HTML comment start or en tag.
    /// </summary>
    public abstract class CppCommentHtmlTag : CppComment
    {
        protected CppCommentHtmlTag(CppCommentKind kind) : base(kind)
        {
        }

        public string TagName { get; set; }

        protected internal abstract override void ToString(StringBuilder builder);
    }
}