using System.Text;

namespace CppAst
{
    /// <summary>
    /// A unary expression.
    /// </summary>
    public class CppUnaryExpression : CppExpression
    {
        public CppUnaryExpression(CppExpressionKind kind) : base(kind)
        {
        }

        /// <summary>
        /// The unary operator as a string.
        /// </summary>
        public string Operator { get; set; }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(Operator);
            if (Arguments != null && Arguments.Count > 0)
            {
                builder.Append(Arguments[0]);
            }
            return builder.ToString();
        }
    }
}