// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Groups voice fingerprints (e.g. Emgu.LiteRT.Models' SpeakerEmbedder) by speaker, as pyannote 3.1 does: agglomerative
    /// clustering with centroid linkage on the length-1 embeddings, merging while the closest two clusters' centroids are
    /// within <see cref="DefaultThreshold"/> (Euclidean; for unit vectors a cosine similarity of about 0.75). Then, as
    /// pyannote's minimum cluster size, a "speaker" with too little speech joins the nearest one that has enough: a single
    /// odd stretch of a voice made a speaker of its own.
    /// </summary>
    public static class SpeakerClustering
    {
        /// <summary>pyannote/speaker-diarization-3.1's threshold.</summary>
        public const double DefaultThreshold = 0.7045654963945799;

        /// <summary>
        /// The least speech a speaker has, in seconds, when the lengths are given: a voice heard for less (a 3 s piece of a
        /// long turn split at a pause made a fifth "speaker" of a three-voice meeting played through a Mac's speakers)
        /// joins the nearest speaker.
        /// </summary>
        public const double MinSpeakerSeconds = 5;

        /// <summary>
        /// The speaker of each embedding, numbered 0, 1, 2... in the order each speaker first appears.
        /// </summary>
        /// <param name="embeddings">Length-1 embeddings, all the same length</param>
        /// <param name="threshold">The largest centroid distance merged</param>
        /// <param name="seconds">How much speech each embedding stands for; with it, speakers with less than <paramref name="minSpeakerSeconds"/> join the nearest other</param>
        /// <param name="minSpeakerSeconds">The least speech a speaker has</param>
        public static int[] Cluster(IReadOnlyList<float[]> embeddings, double threshold = DefaultThreshold,
            IReadOnlyList<double> seconds = null, double minSpeakerSeconds = MinSpeakerSeconds)
        {
            if (embeddings == null) throw new ArgumentNullException(nameof(embeddings));
            int n = embeddings.Count;
            var labels = new int[n];
            if (n == 0) return labels;
            int dim = embeddings[0].Length;
            // Each cluster: its centroid, its size and its members.
            var centroids = new double[n][];
            var sizes = new int[n];
            var members = new List<int>[n];
            var active = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (embeddings[i].Length != dim) throw new ArgumentException("The embeddings differ in length.", nameof(embeddings));
                centroids[i] = new double[dim];
                for (int k = 0; k < dim; k++) centroids[i][k] = embeddings[i][k];
                sizes[i] = 1;
                members[i] = new List<int> { i };
                active[i] = true;
            }
            var distance = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    distance[i, j] = distance[j, i] = Distance(centroids[i], centroids[j]);
            // Each cluster's nearest other cluster, refreshed after a merge where it changed.
            var nearest = new int[n];
            for (int i = 0; i < n; i++) nearest[i] = Nearest(i, distance, active);
            for (int remaining = n; remaining > 1; remaining--)
            {
                int a = -1;
                double best = double.MaxValue;
                for (int i = 0; i < n; i++)
                    if (active[i] && nearest[i] >= 0 && distance[i, nearest[i]] < best)
                    {
                        best = distance[i, nearest[i]];
                        a = i;
                    }
                if (a < 0 || best > threshold) break;
                int b = nearest[a];
                // b into a: the centroid weighted by size.
                for (int k = 0; k < dim; k++)
                    centroids[a][k] = (centroids[a][k] * sizes[a] + centroids[b][k] * sizes[b]) / (sizes[a] + sizes[b]);
                sizes[a] += sizes[b];
                members[a].AddRange(members[b]);
                active[b] = false;
                for (int i = 0; i < n; i++)
                    if (active[i] && i != a)
                        distance[i, a] = distance[a, i] = Distance(centroids[i], centroids[a]);
                for (int i = 0; i < n; i++)
                    if (active[i] && (i == a || nearest[i] == a || nearest[i] == b || (nearest[i] >= 0 && distance[i, a] < distance[i, nearest[i]])))
                        nearest[i] = Nearest(i, distance, active);
            }
            if (seconds != null)
                FoldSmall(centroids, members, active, seconds, minSpeakerSeconds);
            // Number the clusters by their first member.
            var order = new List<int>();
            for (int i = 0; i < n; i++)
                if (active[i]) order.Add(i);
            order.Sort((x, y) => Min(members[x]).CompareTo(Min(members[y])));
            for (int c = 0; c < order.Count; c++)
                foreach (int m in members[order[c]]) labels[m] = c;
            return labels;
        }

        // Clusters with too little speech join the nearest cluster with enough (none: nothing changes).
        private static void FoldSmall(double[][] centroids, List<int>[] members, bool[] active,
            IReadOnlyList<double> seconds, double minSeconds)
        {
            if (seconds.Count != active.Length) throw new ArgumentException("A length is needed for each embedding.", nameof(seconds));
            var big = new List<int>();
            var small = new List<int>();
            for (int i = 0; i < active.Length; i++)
            {
                if (!active[i]) continue;
                double total = 0;
                foreach (int m in members[i]) total += seconds[m];
                (total >= minSeconds ? big : small).Add(i);
            }
            if (big.Count == 0) return;
            foreach (int s in small)
            {
                int target = big[0];
                foreach (int b in big)
                    if (Distance(centroids[s], centroids[b]) < Distance(centroids[s], centroids[target]))
                        target = b;
                // Only into a voice it is like: when only one speaker had enough speech, every other piece joined it.
                if (Cosine(centroids[s], centroids[target]) < MinFoldSimilarity) continue;
                members[target].AddRange(members[s]);
                active[s] = false;
            }
        }

        /// <summary>How alike (cosine) a speaker with too little speech must be to the nearest one to join it.</summary>
        public const double MinFoldSimilarity = 0.5;

        /// <summary>How alike (cosine) two pieces in a row must be to be one turn of one speaker.</summary>
        public const double TurnSimilarity = 0.6;

        /// <summary>
        /// The speaker of each of a recording's pieces, in order: pieces in a row that sound alike (cosine at least
        /// <paramref name="turnSimilarity"/>) are first made one turn, its fingerprint their average weighted by speech, and the
        /// turns are clustered (<see cref="Cluster"/>). A short piece's fingerprint is unsteady - through a Mac's speakers and
        /// microphone one voice's 1-5 s pieces were only 0.48-0.75 alike, as alike as two voices - and a turn's is steadier.
        /// </summary>
        /// <param name="embeddings">The pieces' length-1 fingerprints, in the order they were said</param>
        /// <param name="seconds">How much speech each piece has</param>
        /// <param name="threshold">The largest centroid distance merged</param>
        /// <param name="turnSimilarity">How alike pieces in a row must be to be one turn</param>
        public static int[] ClusterSequence(IReadOnlyList<float[]> embeddings, IReadOnlyList<double> seconds,
            double threshold = DefaultThreshold, double turnSimilarity = TurnSimilarity)
        {
            if (embeddings == null) throw new ArgumentNullException(nameof(embeddings));
            if (seconds == null || seconds.Count != embeddings.Count) throw new ArgumentException("A length is needed for each embedding.", nameof(seconds));
            var turnOf = new int[embeddings.Count];
            var sums = new List<double[]>();
            var turnSeconds = new List<double>();
            for (int i = 0; i < embeddings.Count; i++)
            {
                double weight = Math.Max(0.5, seconds[i]);
                if (sums.Count > 0 && Cosine(sums[sums.Count - 1], embeddings[i]) >= turnSimilarity)
                {
                    double[] sum = sums[sums.Count - 1];
                    for (int k = 0; k < sum.Length; k++) sum[k] += weight * embeddings[i][k];
                    turnSeconds[turnSeconds.Count - 1] += seconds[i];
                }
                else
                {
                    sums.Add(embeddings[i].Select(x => weight * x).ToArray());
                    turnSeconds.Add(seconds[i]);
                }
                turnOf[i] = sums.Count - 1;
            }
            int[] turnLabels = Cluster(sums.Select(Unit).ToList(), threshold, turnSeconds);
            return turnOf.Select(t => turnLabels[t]).ToArray();
        }

        private static float[] Unit(double[] v)
        {
            double length = Math.Sqrt(v.Sum(x => x * x));
            return v.Select(x => (float)(length > 0 ? x / length : 0)).ToArray();
        }

        private static double Cosine(double[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int k = 0; k < a.Length; k++) { dot += a[k] * b[k]; na += a[k] * a[k]; nb += b[k] * b[k]; }
            return na > 0 && nb > 0 ? dot / Math.Sqrt(na * nb) : 0;
        }

        private static double Cosine(double[] a, double[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int k = 0; k < a.Length; k++) { dot += a[k] * b[k]; na += a[k] * a[k]; nb += b[k] * b[k]; }
            return na > 0 && nb > 0 ? dot / Math.Sqrt(na * nb) : 0;
        }

        private static int Min(List<int> values)
        {
            int min = int.MaxValue;
            foreach (int v in values) if (v < min) min = v;
            return min;
        }

        private static int Nearest(int i, double[,] distance, bool[] active)
        {
            int best = -1;
            for (int j = 0; j < active.Length; j++)
                if (j != i && active[j] && (best < 0 || distance[i, j] < distance[i, best]))
                    best = j;
            return best;
        }

        private static double Distance(double[] a, double[] b)
        {
            double sum = 0;
            for (int k = 0; k < a.Length; k++)
            {
                double d = a[k] - b[k];
                sum += d * d;
            }
            return Math.Sqrt(sum);
        }
    }
}
