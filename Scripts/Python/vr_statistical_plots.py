#!/usr/bin/env python3
"""
VR Research Statistical Analysis & Plots
==========================================
Generates NASA-TLX comparison bar charts and sickness progression line plots
from the master_research_database.csv.

Usage:
    python vr_statistical_plots.py --master-csv master_research_database.csv --output-dir ./output/
"""

import argparse
import csv
import os
import sys

try:
    import matplotlib
    matplotlib.use('Agg')  # Non-interactive backend
    import matplotlib.pyplot as plt
    import numpy as np
    HAS_MATPLOTLIB = True
except ImportError:
    HAS_MATPLOTLIB = False
    print("[WARN] matplotlib not installed. Install with: pip install matplotlib")


NASA_TLX_DIMENSIONS = [
    'MentalDemand', 'PhysicalDemand', 'TemporalDemand',
    'Performance', 'Effort', 'Frustration'
]


def load_master_csv(csv_path):
    """Load master CSV into list of dicts."""
    rows = []
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            rows.append(row)
    print(f"[INFO] Loaded {len(rows)} participants from {csv_path}")
    return rows


def plot_nasatlx_comparison(rows, output_dir):
    """Plot paired bar chart comparing NASA-TLX between Task 1 and Task 2."""
    if not HAS_MATPLOTLIB:
        print("[SKIP] matplotlib not available")
        return

    fig, ax = plt.subplots(figsize=(14, 7))

    x = np.arange(len(NASA_TLX_DIMENSIONS))
    width = 0.35

    # Collect scores
    t1_scores = []
    t2_scores = []
    for dim in NASA_TLX_DIMENSIONS:
        t1_vals = [int(r.get(f'T1_{dim}', 0)) for r in rows]
        t2_vals = [int(r.get(f'T2_{dim}', 0)) for r in rows]
        t1_scores.append(np.mean(t1_vals) if t1_vals else 0)
        t2_scores.append(np.mean(t2_vals) if t2_vals else 0)

    # Plot bars
    bars1 = ax.bar(x - width/2, t1_scores, width, label='Task 1 (First Task)',
                    color='#3B7DD8', edgecolor='white', linewidth=0.5)
    bars2 = ax.bar(x + width/2, t2_scores, width, label='Task 2 (Second Task)',
                    color='#E8723A', edgecolor='white', linewidth=0.5)

    # Add value labels on bars
    for bar in bars1:
        height = bar.get_height()
        ax.annotate(f'{height:.1f}',
                    xy=(bar.get_x() + bar.get_width() / 2, height),
                    xytext=(0, 3), textcoords="offset points",
                    ha='center', va='bottom', fontsize=9, fontweight='bold')

    for bar in bars2:
        height = bar.get_height()
        ax.annotate(f'{height:.1f}',
                    xy=(bar.get_x() + bar.get_width() / 2, height),
                    xytext=(0, 3), textcoords="offset points",
                    ha='center', va='bottom', fontsize=9, fontweight='bold')

    # Formatting
    ax.set_xlabel('NASA-TLX Dimension', fontsize=12, fontweight='bold')
    ax.set_ylabel('Mean Score (0-10)', fontsize=12, fontweight='bold')
    ax.set_title('NASA-TLX Workload Comparison: Task 1 vs Task 2\n(Mean ± SD across all participants)',
                 fontsize=14, fontweight='bold', pad=15)
    ax.set_xticks(x)
    ax.set_xticklabels([
        'Mental\nDemand', 'Physical\nDemand', 'Temporal\nDemand',
        'Performance', 'Effort', 'Frustration'
    ], fontsize=10)
    ax.set_ylim(0, 11)
    ax.legend(fontsize=11, loc='upper right')
    ax.grid(axis='y', alpha=0.3, linestyle='--')
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)

    # Add error bars (SD)
    t1_stds = []
    t2_stds = []
    for dim in NASA_TLX_DIMENSIONS:
        t1_vals = [int(r.get(f'T1_{dim}', 0)) for r in rows]
        t2_vals = [int(r.get(f'T2_{dim}', 0)) for r in rows]
        t1_stds.append(np.std(t1_vals) if len(t1_vals) > 1 else 0)
        t2_stds.append(np.std(t2_vals) if len(t2_vals) > 1 else 0)

    ax.errorbar(x - width/2, t1_scores, yerr=t1_stds, fmt='none', color='#1a3a6b', capsize=4)
    ax.errorbar(x + width/2, t2_scores, yerr=t2_stds, fmt='none', color='#8b3a0a', capsize=4)

    plt.tight_layout()
    output_path = os.path.join(output_dir, 'nasatlx_comparison.png')
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"[INFO] NASA-TLX comparison chart saved: {output_path}")


def plot_sickness_progression(rows, output_dir):
    """Plot sickness progression line chart (Pre-VR → Task 1 → Task 2)."""
    if not HAS_MATPLOTLIB:
        print("[SKIP] matplotlib not available")
        return

    fig, ax = plt.subplots(figsize=(10, 6))

    participants = [r['ParticipantID'] for r in rows]
    x_labels = ['Pre-VR\n(Screening)', 'After Task 1\n(Mid-break)', 'After Task 2\n(Final)']

    # Use different colors for each participant
    colors = ['#3B7DD8', '#E8723A', '#4CAF50', '#9C27B0', '#FF5722',
              '#00BCD4', '#795548', '#607D8B']

    for i, row in enumerate(rows):
        pid = row['ParticipantID']
        # Pre-VR sickness assumed 0 (screening)
        pre_vr = 0
        t1_sickness = int(row.get('T1_Sickness', 0))
        t2_sickness = int(row.get('T2_Sickness', 0))

        y_values = [pre_vr, t1_sickness, t2_sickness]
        color = colors[i % len(colors)]

        ax.plot(x_labels, y_values, marker='o', linewidth=2, markersize=8,
                label=pid, color=color, alpha=0.8)

        # Add value labels
        for j, val in enumerate(y_values):
            ax.annotate(str(val), (j, val), textcoords="offset points",
                       xytext=(8, 5), fontsize=9, color=color, fontweight='bold')

    # Formatting
    ax.set_xlabel('Measurement Point', fontsize=12, fontweight='bold')
    ax.set_ylabel('Sickness Score (FMS: 0=None, 10=Severe)', fontsize=12, fontweight='bold')
    ax.set_title('Cybersickness Progression Across Experiment Phases\n(Score 0-10, Higher = More Severe)',
                 fontsize=14, fontweight='bold', pad=15)
    ax.set_ylim(-0.5, 10.5)
    ax.legend(fontsize=10, loc='upper left', ncol=2)
    ax.grid(alpha=0.3, linestyle='--')
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)

    # Add shaded regions
    ax.axhspan(-0.5, 3.5, alpha=0.05, color='green', label='_nolegend_')
    ax.axhspan(3.5, 6.5, alpha=0.05, color='yellow', label='_nolegend_')
    ax.axhspan(6.5, 10.5, alpha=0.05, color='red', label='_nolegend_')

    # Add zone labels
    ax.text(2.3, 1.5, 'Mild', fontsize=8, color='green', alpha=0.6, style='italic')
    ax.text(2.3, 5.0, 'Moderate', fontsize=8, color='orange', alpha=0.6, style='italic')
    ax.text(2.3, 8.5, 'Severe', fontsize=8, color='red', alpha=0.6, style='italic')

    plt.tight_layout()
    output_path = os.path.join(output_dir, 'sickness_progression.png')
    plt.savefig(output_path, dpi=150, bbox_inches='tight')
    plt.close()
    print(f"[INFO] Sickness progression chart saved: {output_path}")


def print_summary_table(rows):
    """Print a summary statistics table."""
    print("\n" + "=" * 80)
    print("RESEARCH DATA SUMMARY")
    print("=" * 80)

    print(f"\n{'Participant':<12} {'Cond':<8} {'T1_Dur':>8} {'T2_Dur':>8} {'T1_Grab':>8} {'T2_Grab':>8} {'T1_MD':>6} {'T2_MD':>6} {'T1_Sick':>8} {'T2_Sick':>8}")
    print("-" * 80)

    for row in rows:
        pid = row['ParticipantID']
        cond = f"{row.get('Task1_Condition', '?')}/{row.get('Task2_Condition', '?')}"
        t1_dur = row.get('T1_Duration_Sec', '0')
        t2_dur = row.get('T2_Duration_Sec', '0')
        t1_grab = row.get('T1_GrabCount', '0')
        t2_grab = row.get('T2_GrabCount', '0')
        t1_md = row.get('T1_MentalDemand', '0')
        t2_md = row.get('T2_MentalDemand', '0')
        t1_sick = row.get('T1_Sickness', '0')
        t2_sick = row.get('T2_Sickness', '0')

        print(f"{pid:<12} {cond:<8} {t1_dur:>8} {t2_dur:>8} {t1_grab:>8} {t2_grab:>8} {t1_md:>6} {t2_md:>6} {t1_sick:>8} {t2_sick:>8}")

    print("=" * 80)


def main():
    parser = argparse.ArgumentParser(description='VR Research Statistical Plots')
    parser.add_argument('--master-csv', required=True, help='Path to master_research_database.csv')
    parser.add_argument('--output-dir', default='./output', help='Output directory for plots')
    args = parser.parse_args()

    os.makedirs(args.output_dir, exist_ok=True)

    rows = load_master_csv(args.master_csv)
    print_summary_table(rows)

    print("\n[STEP 2] Generating plots...")
    plot_nasatlx_comparison(rows, args.output_dir)
    plot_sickness_progression(rows, args.output_dir)

    print("\n[DONE] Analysis complete!")


if __name__ == '__main__':
    main()
