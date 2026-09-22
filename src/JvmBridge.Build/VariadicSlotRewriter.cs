using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JvmBridge.Build;

internal sealed class VariadicSlotRewriter : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        if (node.Declaration.Type is not FunctionPointerTypeSyntax)
            return node;

        foreach (AttributeListSyntax list in node.AttributeLists)
        {
            foreach (AttributeSyntax attribute in list.Attributes)
            {
                if (attribute.Name.ToString() != "NativeTypeName" || attribute.ArgumentList is null)
                    continue;

                foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
                {
                    if (argument.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                        continue;

                    string nativeType = literal.Token.ValueText;

                    if (nativeType.Contains(value: "va_list", StringComparison.Ordinal) || nativeType.Contains(value: "...", StringComparison.Ordinal))
                        return node.WithDeclaration(node.Declaration.WithType(SyntaxFactory.ParseTypeName(text: "nint").WithTriviaFrom(node.Declaration.Type)));
                }
            }
        }

        return node;
    }

    internal static string Rewrite(string source)
    {
        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();

        return new VariadicSlotRewriter().Visit(root)?.ToFullString() ?? throw new InvalidOperationException(message: "Binding syntax rewriting produced no output.");
    }
}
