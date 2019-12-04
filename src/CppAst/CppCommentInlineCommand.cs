using System.Text;

namespace CppAst
{
    /// <summary>
    /// An inline comment command.
    /// </summary>
    public class CppCommentInlineCommand : CppCommentCommand
    {
        public CppCommentInlineCommand() : base(CppCommentKind.InlineCommand)
        {
        }

        public CppCommentInlineCommandRenderKind RenderKind { get; set; }

        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            ChildrenToString(builder);
        }
    }
}