using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment for a function/method parameter.
    /// </summary>
    public class CppCommentParamCommand : CppCommentCommand
    {
        public CppCommentParamCommand() : base(CppCommentKind.ParamCommand)
        {
        }

        /// <summary>
        /// Gets or sets the name of the parameter.
        /// </summary>
        public string ParamName { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating if the <see cref="ParamIndex"/> is valid.
        /// </summary>
        public bool IsParamIndexValid { get; set; }

        /// <summary>
        /// Gets or sets the index of this parameter in the function parameters.
        /// </summary>
        public int ParamIndex { get; set; }
        
        /// <summary>
        /// Gets or sets the direction of this parameter (in, out, inout).
        /// </summary>
        public CppCommentParamDirection Direction { get; set; }

        /// <summary>
        /// Gets or sets a boolean indicating if <see cref="Direction"/> was explicitly specified.
        /// </summary>
        public bool IsDirectionExplicit { get; set; }

        protected internal override void ToString(StringBuilder builder)
        {
            base.ToString(builder);
            builder.Append(ParamName);
            builder.Append(" ");
            ChildrenToString(builder);
        }
    }
}