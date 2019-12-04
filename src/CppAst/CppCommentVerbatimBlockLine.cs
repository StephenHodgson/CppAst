using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment for a verbatim line inside a verbatim block.
    /// </summary>
    public class CppCommentVerbatimBlockLine : CppCommentTextBase
    {
        public CppCommentVerbatimBlockLine() : base(CppCommentKind.VerbatimBlockLine)
        {
        }

        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            builder.AppendLine();
        }

    }
}