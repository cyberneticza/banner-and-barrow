using System.Reflection;
using BannerAndBarrow.Simulation;
using BannerAndBarrow.Simulation.Math;

namespace BannerAndBarrow.Tests;

public class FixTests
{
    [Fact]
    public void Arithmetic_is_exact_for_simple_values()
    {
        var a = Fix.FromInt(3);
        var b = Fix.Half;
        Assert.Equal(Fix.FromRatio(7, 2), a + b);
        Assert.Equal(Fix.FromRatio(3, 2), a * b);
        Assert.Equal(Fix.FromInt(6), a / b);
        Assert.Equal(-1, Fix.FromRatio(-1, 2).FloorToInt());
        Assert.Equal(2, Fix.FromRatio(5, 2).FloorToInt());
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(144, 12)]
    [InlineData(1, 1)]
    public void Sqrt_of_perfect_squares_is_exact(int value, int root) =>
        Assert.Equal(Fix.FromInt(root), Fix.Sqrt(Fix.FromInt(value)));

    [Fact]
    public void Sqrt_is_close_for_non_squares()
    {
        var root2 = Fix.Sqrt(Fix.FromInt(2));
        Assert.InRange(root2.ToDecimal(), 1.41419m, 1.41423m); // 16 fractional bits ≈ 0.000015 precision
    }

    [Fact]
    public void Large_world_distances_do_not_overflow()
    {
        var a = new FixVec2(Fix.FromInt(0), Fix.FromInt(0));
        var b = new FixVec2(Fix.FromInt(6000), Fix.FromInt(6000));
        Assert.InRange(FixVec2.Distance(a, b).ToDecimal(), 8485m, 8486m);
    }

    [Fact]
    public void FromDecimal_round_trips()
    {
        Assert.Equal(0.25m, Fix.FromDecimal(0.25m).ToDecimal());
        Assert.Equal(1.5m, Fix.FromDecimal(1.5m).ToDecimal());
    }

    [Fact]
    public void Random_is_deterministic_per_seed()
    {
        var a = new DeterministicRandom(99);
        var b = new DeterministicRandom(99);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
    }
}

/// <summary>The simulation must stay deterministic: no float/double anywhere in its types or locals.</summary>
public class NoFloatingPointTests
{
    [Fact]
    public void Simulation_assembly_declares_no_float_or_double()
    {
        var assembly = typeof(GameState).Assembly;
        var offenders = new List<string>();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in assembly.GetTypes())
        {
            foreach (var f in type.GetFields(all))
                if (IsFloat(f.FieldType)) offenders.Add($"{type.FullName}.{f.Name}");
            foreach (var m in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (m is MethodInfo mi && IsFloat(mi.ReturnType)) offenders.Add($"{type.FullName}.{m.Name} returns float");
                foreach (var p in m.GetParameters())
                    if (IsFloat(p.ParameterType)) offenders.Add($"{type.FullName}.{m.Name}({p.Name})");
                var body = m.GetMethodBody();
                if (body == null) continue;
                foreach (var local in body.LocalVariables)
                    if (IsFloat(local.LocalType)) offenders.Add($"{type.FullName}.{m.Name} local #{local.LocalIndex}");
            }
        }

        Assert.True(offenders.Count == 0, "Floating point found in simulation:\n" + string.Join("\n", offenders));
    }

    private static bool IsFloat(Type t)
    {
        if (t.IsByRef || t.IsArray || t.IsPointer) t = t.GetElementType()!;
        return t == typeof(float) || t == typeof(double);
    }
}
