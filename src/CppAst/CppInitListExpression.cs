using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ Init list expression `{ a, b, c }`
    /// </summary>
    public class CppInitListExpression : CppExpression
    {
        public CppInitListExpression() : base(CppExpressionKind.InitList)
        {
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append('{');
            ArgumentsSeparatedByCommaToString(builder);
            builder.Append('}');
            return builder.ToString();
        }
    }
}