// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;

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

        /// <summary>The least speech a speaker has, in seconds, when the lengths are given.</summary>
        public const double MinSpeakerSeconds = 3;

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
                members[target].AddRange(members[s]);
                active[s] = false;
            }
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
