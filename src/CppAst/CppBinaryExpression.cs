using System.Text;

namespace CppAst
{
    /// <summary>
    /// A binary expression
    /// </summary>
    public class CppBinaryExpression : CppExpression
    {
        public CppBinaryExpression(CppExpressionKind kind) : base(kind)
        {
        }

        /// <summary>
        /// The binary operator as a string.
        /// </summary>
        public string Operator { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            var builder = new StringBuilder();

            if (Arguments != null && Arguments.Count > 0)
            {
                builder.Append(Arguments[0]);
            }

            builder.Append(" ");
            builder.Append(Operator);
            builder.Append(" ");

            if (Arguments != null && Arguments.Count > 1)
            {
                builder.Append(Arguments[1]);
            }

            return builder.ToString();
        }
    }
}