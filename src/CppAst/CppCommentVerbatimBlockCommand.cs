using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment for a verbatim block command.
    /// </summary>
    public class CppCommentVerbatimBlockCommand : CppCommentCommand
    {
        public CppCommentVerbatimBlockCommand() : base(CppCommentKind.VerbatimBlockCommand)
        {
        }

        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            ChildrenToString(builder);
            builder.AppendLine($"@end{CommandName}");
        }
    }
}