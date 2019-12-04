using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A comment that is a command (e.g `@param arg1`)
    /// </summary>
    public abstract class CppCommentCommand : CppComment
    {
        protected CppCommentCommand(CppCommentKind kind) : base(kind)
        {
            Arguments = new List<string>();
        }

        public string CommandName { get; set; }

        public List<string> Arguments { get; }
        
        protected internal override void ToString(StringBuilder builder)
        {
            builder.Append($"@{CommandName}");
            for (var index = 0; index < Arguments.Count; index++)
            {
                var argument = Arguments[index];
                builder.Append(" ");
                builder.Append(argument);
            }
            builder.Append(" ");
        }
    }
}