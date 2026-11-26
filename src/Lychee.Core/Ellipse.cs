namespace Lychee;

// Axes are r(1+e) and r(1-e)
public readonly record struct Ellipse(double Cx, double Cy, double R, double E, double Theta)
{
    // Relative axis difference a disc may have and still be round
    const double Round = 0.01;

    // Signed distance in px, positive outside
    public double Dist(double x, double y)
    {
        double c = Math.Cos(Theta),
            s = Math.Sin(Theta);
        double u = (x - Cx) * c + (y - Cy) * s,
            v = (y - Cy) * c - (x - Cx) * s;
        return (Math.Sqrt(Sq(u / (R * (1 + E))) + Sq(v / (R * (1 - E)))) - 1) * R;
    }

    static double Sq(double v) => v * v;

    double[] ToArray() => [Cx, Cy, R, E, Theta];

    static Ellipse From(double[] p) => new(p[0], p[1], p[2], p[3], p[4]);

    public static Ellipse Fit(double[] x, double[] y, Ellipse? start = null)
    {
        var p0 = start ?? Circle(x, y);
        double k = Math.Sqrt(x.Length) / Round;
        // Soft-L1 to settle near the curve, then Cauchy to all but ignore outliers
        var p = Solve(p0.ToArray(), Residuals(x, y, k, z => 2 * (Math.Sqrt(1 + z) - 1)), x.Length + 1);
        return From(Solve(p, Residuals(x, y, k, double.LogP1), x.Length + 1));
    }

    static Action<double[], double[]> Residuals(double[] x, double[] y, double k, Func<double, double> rho) =>
        (p, r) =>
        {
            var e = From(p);
            Parallel.For(0, x.Length, i =>
            {
                double d = e.Dist(x[i], y[i]);
                r[i] = Math.Sign(d) * Math.Sqrt(rho(d * d));
            });
            r[x.Length] = k * p[3];
        };

    static Ellipse Circle(double[] x, double[] y)
    {
        double mx = x.Average(),
            my = y.Average();
        var a = new double[3, 4];
        for (int i = 0; i < x.Length; i++)
        {
            double u = x[i] - mx,
                v = y[i] - my;
            double[] row = [2 * u, 2 * v, 1, u * u + v * v];
            for (int j = 0; j < 3; j++)
            {
                for (int l = 0; l < 4; l++)
                    a[j, l] += row[j] * row[l];
            }
        }
        var s = Gauss(a, 3);
        return new(s[0] + mx, s[1] + my, Math.Sqrt(s[2] + s[0] * s[0] + s[1] * s[1]), 0, 0);
    }

    static double[] Solve(double[] p, Action<double[], double[]> residuals, int m)
    {
        int n = p.Length;
        var r = new double[m];
        residuals(p, r);
        double cost = r.Sum(v => v * v),
            lambda = 1e-3;
        var cols = new double[n][];
        for (int j = 0; j < n; j++)
            cols[j] = new double[m];
        var trial = new double[m];
        for (int iter = 0; iter < 200; iter++)
        {
            Jacobian(p, residuals, r, cols);
            var (jtj, jtr) = NormalEquations(cols, r);
            double maxDiag = Enumerable.Range(0, n).Max(j => jtj[j, j]);
            bool improved = false;
            while (lambda < 1e12)
            {
                var step = Gauss(Damped(jtj, jtr, lambda, maxDiag), n);
                var next = p.Zip(step, (v, d) => v + d).ToArray();
                residuals(next, trial);
                double nextCost = trial.Sum(v => v * v);
                if (nextCost < cost && double.IsFinite(nextCost))
                {
                    bool done = cost - nextCost <= 1e-12 * cost;
                    (p, cost) = (next, nextCost);
                    Array.Copy(trial, r, m);
                    lambda = Math.Max(lambda / 3, 1e-12);
                    improved = !done;
                    break;
                }
                lambda *= 4;
            }
            if (!improved)
                break;
        }
        return p;
    }

    static void Jacobian(double[] p, Action<double[], double[]> residuals, double[] r, double[][] cols)
    {
        for (int j = 0; j < p.Length; j++)
        {
            var q = (double[])p.Clone();
            double h = 1e-7 * Math.Max(1, Math.Abs(p[j]));
            q[j] += h;
            residuals(q, cols[j]);
            for (int i = 0; i < r.Length; i++)
                cols[j][i] = (cols[j][i] - r[i]) / h;
        }
    }

    static (double[,] JtJ, double[] JtR) NormalEquations(double[][] cols, double[] r)
    {
        int n = cols.Length;
        var jtj = new double[n, n];
        var jtr = new double[n];
        for (int a = 0; a < n; a++)
        {
            for (int b = a; b < n; b++)
            {
                double s = 0;
                for (int i = 0; i < r.Length; i++)
                    s += cols[a][i] * cols[b][i];
                jtj[a, b] = jtj[b, a] = s;
            }
            double g = 0;
            for (int i = 0; i < r.Length; i++)
                g += cols[a][i] * r[i];
            jtr[a] = g;
        }
        return (jtj, jtr);
    }

    static double[,] Damped(double[,] jtj, double[] jtr, double lambda, double maxDiag)
    {
        int n = jtr.Length;
        var sys = new double[n, n + 1];
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++)
                sys[a, b] = jtj[a, b];
            sys[a, a] += lambda * (jtj[a, a] + 1e-12 * maxDiag);
            sys[a, n] = -jtr[a];
        }
        return sys;
    }

    static double[] Gauss(double[,] a, int n)
    {
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int i = c + 1; i < n; i++)
            {
                if (Math.Abs(a[i, c]) > Math.Abs(a[piv, c]))
                    piv = i;
            }

            for (int j = 0; j <= n; j++)
                (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
            if (a[c, c] == 0)
                continue;
            for (int i = c + 1; i < n; i++)
            {
                double f = a[i, c] / a[c, c];
                for (int j = c; j <= n; j++)
                    a[i, j] -= f * a[c, j];
            }
        }
        var s = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double v = a[i, n];
            for (int j = i + 1; j < n; j++)
                v -= a[i, j] * s[j];
            s[i] = a[i, i] == 0 ? 0 : v / a[i, i];
        }
        return s;
    }
}
