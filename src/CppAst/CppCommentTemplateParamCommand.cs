using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment for a template parameter command.
    /// </summary>
    public class CppCommentTemplateParamCommand : CppCommentCommand
    {
        public CppCommentTemplateParamCommand() : base(CppCommentKind.TemplateParamCommand)
        {
        }

        /// <summary>
        /// Gets or sets the name of the parameter.
        /// </summary>
        public string ParamName { get; set; }

        /// <summary>
        /// Depth or this parameter.
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating if this <see cref="Index"/> is valid
        /// </summary>
        public bool IsPositionValid { get; set; }

        /// <summary>
        /// Gets or sets the index of this template parameter.
        /// </summary>
        public int Index { get; set; }

        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            builder.Append(ParamName);
            builder.Append(" ");
            ChildrenToString(builder);
        }
    }
}