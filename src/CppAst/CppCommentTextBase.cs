using System.Text;

namespace CppAst
{
    /// <summary>
    /// Base class for all text based comments.
    /// </summary>
    public abstract class CppCommentTextBase : CppComment
    {
        protected CppCommentTextBase(CppCommentKind kind) : base(kind)
        {
        }

        public string Text { get; set; }

        protected internal override void ToString(StringBuilder builder)
        {
            builder.Append(Text);
        }
    }
}