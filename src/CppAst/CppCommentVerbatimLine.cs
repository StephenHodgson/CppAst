using System.Text;

namespace CppAst
{
    /// <summary>
    /// A verbatim line comment.
    /// </summary>
    public class CppCommentVerbatimLine : CppCommentTextBase
    {
        public CppCommentVerbatimLine() : base(CppCommentKind.VerbatimLine)
        {
        }

        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            builder.AppendLine();
        }
    }
}