using System.Linq.Expressions;
using System.Reflection;

using JvmBridge.ClassFiles;

namespace JvmBridge.Agents;

/// <summary>
/// A <see cref="ClassPatch.When"/> condition, read from its expression when the patch is built: the tests of <see cref="MethodBody"/> with their arguments evaluated then, combined with and, or and not. It is evaluated without compiling the expression, so it works
/// in a NativeAOT agent, and it prints in the style of the expression for skip reasons.
/// </summary>
internal sealed class BodyCondition
{
    private const int And = 3;
    private const int Constant = 6;
    private const int Field = 1;
    private const int Invocation = 0;
    private const int Not = 5;
    private const int Or = 4;
    private const int Construction = 2;

    private readonly int _kind;
    private readonly BodyCondition? _left;
    private readonly BodyCondition? _right;
    private readonly JavaMethod _method;
    private readonly string _first;
    private readonly string _second;
    private readonly bool _value;

    private BodyCondition(int kind, BodyCondition? left = null, BodyCondition? right = null, JavaMethod method = default, string first = "", string second = "", bool value = false)
    {
        _kind = kind;
        _left = left;
        _right = right;
        _method = method;
        _first = first;
        _second = second;
        _value = value;
    }

    /// <summary>Reads a condition from its expression, evaluating the arguments of its tests now.</summary>
    /// <exception cref="ArgumentException">The expression uses something other than the tests of <see cref="MethodBody"/>, constants, and <c>&amp;&amp;</c>, <c>||</c> and <c>!</c>, or a test names a malformed member.</exception>
    internal static BodyCondition Read(Expression<Func<MethodBody, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return Read(expression.Body, expression.Parameters[0]);
    }

    /// <summary>Evaluates the condition over the code of the methods it looks at.</summary>
    internal bool Holds(MethodBody body)
    {
        if (_kind == And)
            return Child(_left).Holds(body) && Child(_right).Holds(body);

        if (_kind == Or)
            return Child(_left).Holds(body) || Child(_right).Holds(body);

        if (_kind == Not)
            return !Child(_left).Holds(body);

        if (_kind == Invocation)
            return body.Calls(_method);

        if (_kind == Field)
            return body.AccessesField(_first, _second);

        return _kind == Construction ? body.Creates(_first) : _value;
    }

    /// <summary>Prints the condition, for example <c>writeLock() &amp;&amp; put() &amp;&amp; !putIfAbsent()</c>.</summary>
    public override string ToString() => Print(parentKind: -1);

    private static BodyCondition Child(BodyCondition? child) => child ?? throw new InvalidOperationException(message: "A combined condition lacks a part.");

    private static object? Evaluate(Expression expression, ParameterExpression body)
    {
        if (expression is ConstantExpression constant)
            return constant.Value;

        if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
            return Evaluate(conversion.Operand, body);

        if (expression is MemberExpression member)
        {
            object? target = member.Expression is null ? null : Evaluate(member.Expression, body);

            return member.Member switch
            {
                FieldInfo field => field.GetValue(target),
                PropertyInfo property => property.GetValue(target),
                _ => throw Unsupported(expression),
            };
        }

        if (expression is MethodCallExpression call && call.Object != body && !call.Arguments.Contains(body))
            return call.Method.Invoke(call.Object is null ? null : Evaluate(call.Object, body), [.. call.Arguments.Select(argument => Evaluate(argument, body))]);

        if (expression is NewExpression creation && creation.Constructor is { } constructor)
            return constructor.Invoke([.. creation.Arguments.Select(argument => Evaluate(argument, body))]);

        throw Unsupported(expression);
    }

    private static BodyCondition Read(Expression expression, ParameterExpression body)
    {
        if (expression.NodeType == ExpressionType.AndAlso && expression is BinaryExpression both)
            return new BodyCondition(And, Read(both.Left, body), Read(both.Right, body));

        if (expression.NodeType == ExpressionType.OrElse && expression is BinaryExpression either)
            return new BodyCondition(Or, Read(either.Left, body), Read(either.Right, body));

        if (expression.NodeType == ExpressionType.Not && expression is UnaryExpression negation)
            return new BodyCondition(Not, Read(negation.Operand, body));

        if (expression is ConstantExpression { Value: bool value })
            return new BodyCondition(Constant, value: value);

        if (expression is not MethodCallExpression test || test.Object != body || test.Method.DeclaringType != typeof(MethodBody))
            throw Unsupported(expression);

        object?[] arguments = [.. test.Arguments.Select(argument => Evaluate(argument, body))];

        if (test.Method.Name == nameof(MethodBody.Calls) && arguments[0] is JavaMethod method)
        {
            bool valid = method.Owner is { Length: > 0 } && method.Name is { Length: > 0 } && method.Descriptor is ['(', ..] && method.Descriptor.Contains(')', StringComparison.Ordinal);

            return valid ? new BodyCondition(Invocation, method: method) : throw new ArgumentException($"The condition names a malformed method: {method}.", nameof(expression));
        }

        string first = arguments[0] as string ?? string.Empty;
        string second = arguments.Length > 1 ? arguments[1] as string ?? string.Empty : string.Empty;

        if (first.Length == 0 || first.Contains('.', StringComparison.Ordinal) || (test.Method.Name == nameof(MethodBody.AccessesField) && second.Length == 0))
            throw new ArgumentException($"The condition {test.Method.Name} needs an internal class name with slashes, and a field name for AccessesField.", nameof(expression));

        return test.Method.Name == nameof(MethodBody.AccessesField) ? new BodyCondition(Field, first: first, second: second) : new BodyCondition(Construction, first: first);
    }

    private static ArgumentException Unsupported(Expression expression)
        => new($"A When condition combines the tests of MethodBody (Calls, AccessesField, Creates) with &&, || and !, and their arguments must not depend on the body; '{expression}' is not one of those.", nameof(expression));

    private string Print(int parentKind)
    {
        string text = _kind switch
        {
            And => $"{Child(_left).Print(And)} && {Child(_right).Print(And)}",
            Or => $"{Child(_left).Print(Or)} || {Child(_right).Print(Or)}",
            Not => "!" + Child(_left).Print(Not),
            Invocation => _method.Name + "()",
            Field => $"field {_first[(_first.LastIndexOf('/') + 1)..]}.{_second}",
            Construction => "new " + _first[(_first.LastIndexOf('/') + 1)..],
            _ => _value ? "true" : "false",
        };

        // And binds tighter than or, so only an or inside an and, or anything combined under a not, needs parentheses.
        bool wrap = (_kind == Or && parentKind == And) || (_kind is And or Or && parentKind == Not);

        return wrap ? "(" + text + ")" : text;
    }
}
