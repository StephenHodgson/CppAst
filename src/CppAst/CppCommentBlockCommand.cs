using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment block command (`@code ... @endcode`)
    /// </summary>
    public class CppCommentBlockCommand : CppCommentCommand
    {
        public CppCommentBlockCommand() : base(CppCommentKind.BlockCommand)
        {
        }
        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            ChildrenToString(builder);
        }
    }
}