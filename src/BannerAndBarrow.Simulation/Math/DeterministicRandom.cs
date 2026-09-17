namespace BannerAndBarrow.Simulation.Math;

/// <summary>Seeded xorshift64* generator. The only source of randomness the simulation may use.</summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        // Warm up so nearby seeds diverge quickly.
        for (int i = 0; i < 8; i++) NextULong();
    }

    public ulong NextULong()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    public int Next(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextULong() % (ulong)maxExclusive);

    /// <summary>Uniform integer in [min, maxExclusive).</summary>
    public int Next(int min, int maxExclusive) => min + Next(maxExclusive - min);

    /// <summary>Uniform value in [0, 1).</summary>
    public Fix NextFix() => Fix.FromRaw((long)(NextULong() >> (64 - Fix.FractionBits)));

    public Fix NextFix(Fix min, Fix max) => min + (max - min) * NextFix();

    public bool Chance(Fix probability) => NextFix() < probability;

    public FixVec2 InsideUnitDisc()
    {
        while (true)
        {
            var v = new FixVec2(NextFix() * 2 - Fix.One, NextFix() * 2 - Fix.One);
            if (v.LengthSquared <= Fix.One) return v;
        }
    }
}

/// <summary>Stateless integer hashing used for procedural noise.</summary>
public static class IntHash
{
    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed * 0x27D4EB2Du;
            h ^= (uint)x * 0x165667B1u;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0x9E3779B1u;
            h *= 0x85EBCA77u;
            h ^= h >> 16;
            h *= 0xC2B2AE3Du;
            h ^= h >> 13;
            return h;
        }
    }

    /// <summary>Hash mapped to a Fix in [0, 1).</summary>
    public static Fix HashToFix(int x, int y, int seed) => Fix.FromRaw(Hash(x, y, seed) >> (32 - Fix.FractionBits));
}
