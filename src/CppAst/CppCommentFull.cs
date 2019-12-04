using System.Text;

namespace CppAst
{
    /// <summary>
    /// Top level comment container.
    /// </summary>
    public class CppCommentFull : CppComment
    {
        public CppCommentFull() : base(CppCommentKind.Full)
        {
        }

        protected internal override void ToString(StringBuilder builder)
        {
            ChildrenToString(builder);
        }

        public override string ToString()
        {
            return base.ToString().TrimEnd();
        }
    }
}