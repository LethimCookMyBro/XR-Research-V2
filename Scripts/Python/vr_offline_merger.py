#!/usr/bin/env python3
"""
VR Research Offline-to-Online Data Merger
==========================================
Merges Quest tracking data (movement CSV + events JSON) with external tablet survey
data (NASA-TLX, demographics) into a single Master CSV for SPSS/R analysis.

Also auto-generates R Script and SPSS Syntax for immediate statistical analysis.

Usage:
    python vr_offline_merger.py --tablet-survey external_tablet_survey.csv --quest-data-dir ./vr_research_data/
    python vr_offline_merger.py --tablet-survey external_tablet_survey.csv --quest-data-dir ./vr_research_data/ --output master.csv
"""

import argparse
import csv
import json
import os
import sys
import math
from pathlib import Path
from datetime import datetime


def load_tablet_survey(csv_path):
    """Load external tablet survey CSV (demographics + NASA-TLX scores)."""
    participants = {}
    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        for row in reader:
            pid = row['ParticipantID'].strip()
            participants[pid] = {
                'ParticipantID': pid,
                'Age': int(row.get('Age', 0)),
                'Gender': row.get('Gender', ''),
                'VRExperience': row.get('VRExperience', ''),
                'T1_MentalDemand': int(row.get('T1_MentalDemand', 0)),
                'T1_PhysicalDemand': int(row.get('T1_PhysicalDemand', 0)),
                'T1_TemporalDemand': int(row.get('T1_TemporalDemand', 0)),
                'T1_Performance': int(row.get('T1_Performance', 0)),
                'T1_Effort': int(row.get('T1_Effort', 0)),
                'T1_Frustration': int(row.get('T1_Frustration', 0)),
                'T1_Presence': int(row.get('T1_Presence', 0)),
                'T1_Sickness': int(row.get('T1_Sickness', 0)),
                'T2_MentalDemand': int(row.get('T2_MentalDemand', 0)),
                'T2_PhysicalDemand': int(row.get('T2_PhysicalDemand', 0)),
                'T2_TemporalDemand': int(row.get('T2_TemporalDemand', 0)),
                'T2_Performance': int(row.get('T2_Performance', 0)),
                'T2_Effort': int(row.get('T2_Effort', 0)),
                'T2_Frustration': int(row.get('T2_Frustration', 0)),
                'T2_Presence': int(row.get('T2_Presence', 0)),
                'T2_Sickness': int(row.get('T2_Sickness', 0)),
                'Task1_Condition': row.get('Task1_Condition', 'A'),
                'Task2_Condition': row.get('Task2_Condition', 'B'),
            }
    print(f"[INFO] Loaded {len(participants)} participants from tablet survey.")
    return participants


def analyze_quest_session(session_dir):
    """Analyze a single Quest session directory and return metrics."""
    metrics = {}

    csv_files = list(Path(session_dir).glob('*movement.csv'))
    if not csv_files:
        csv_files = list(Path(session_dir).glob('movement_data.csv'))

    if csv_files:
        rows = []
        with open(csv_files[0], 'r') as f:
            reader = csv.DictReader(f)
            for row in reader:
                rows.append(row)

        if rows:
            t_start = float(rows[0]['TimestampMs'])
            t_end = float(rows[-1]['TimestampMs'])
            metrics['Duration_Sec'] = round((t_end - t_start) / 1000.0, 2)

            total_dist = 0
            for i in range(1, len(rows)):
                dx = float(rows[i]['HeadPosX']) - float(rows[i-1]['HeadPosX'])
                dy = float(rows[i]['HeadPosY']) - float(rows[i-1]['HeadPosY'])
                dz = float(rows[i]['HeadPosZ']) - float(rows[i-1]['HeadPosZ'])
                total_dist += math.sqrt(dx*dx + dy*dy + dz*dz)
            metrics['Distance_M'] = round(total_dist, 2)

            metrics['AvgSpeed'] = round(metrics['Distance_M'] / metrics['Duration_Sec'], 2) if metrics['Duration_Sec'] > 0 else 0
            metrics['FrameCount'] = len(rows)

    json_files = list(Path(session_dir).glob('*events.json'))
    if not json_files:
        json_files = list(Path(session_dir).glob('interaction_events.json'))

    if json_files:
        with open(json_files[0], 'r') as f:
            events = json.load(f)
        metrics['GrabCount'] = sum(1 for e in events if e.get('EventType') == 'Grab')
        metrics['ReleaseCount'] = sum(1 for e in events if e.get('EventType') == 'Release')
        metrics['TeleportCount'] = sum(1 for e in events if e.get('EventType') == 'Teleport')
    else:
        metrics['GrabCount'] = 0
        metrics['ReleaseCount'] = 0
        metrics['TeleportCount'] = 0

    return metrics


def find_quest_sessions(quest_data_dir, participant_id):
    """Find session directories for a participant."""
    sessions = []
    quest_path = Path(quest_data_dir)
    for item in quest_path.iterdir():
        if item.is_dir() and item.name.startswith(participant_id):
            sessions.append(item)
    sessions.sort(key=lambda x: x.name)
    return sessions


def generate_r_script(output_csv_path, output_dir):
    """Auto-generate R Script for Repeated Measures ANOVA analysis."""
    csv_abs = os.path.abspath(output_csv_path).replace('\\', '/')
    r_script = f'''# ============================================================
# VR Research: Repeated Measures ANOVA Analysis
# Auto-generated by vr_offline_merger.py
# Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}
# ============================================================
# How to run:
#   1. Open RStudio
#   2. File > Open File > select this script
#   3. Click "Source" or press Ctrl+Shift+Enter
# ============================================================

# --- Install packages if needed ---
if (!require("ez")) install.packages("ez")
if (!require("ggplot2")) install.packages("ggplot2")
if (!require("dplyr")) install.packages("dplyr")
if (!require("tidyr")) install.packages("tidyr")

library(ez)
library(ggplot2)
library(dplyr)
library(tidyr)

# --- Load Data ---
data <- read.csv("{csv_abs}", stringsAsFactors = FALSE)
cat("Loaded", nrow(data), "participants\\n")

# --- Reshape to Long Format for ANOVA ---
tlx_dims <- c("MentalDemand", "PhysicalDemand", "TemporalDemand",
               "Performance", "Effort", "Frustration")

long_data <- data %>%
  select(ParticipantID, Task1_Condition, ends_with(tlx_dims)) %>%
  pivot_longer(
    cols = -c(ParticipantID, Task1_Condition),
    names_to = c("Condition", "Dimension"),
    names_pattern = "^(T[12])_(.*)$",
    values_to = "Score"
  ) %>%
  mutate(
    Task = ifelse(Condition == "T1", Task1_Condition,
                  ifelse(Task1_Condition == "A", "B", "A")),
    Condition = factor(Condition, levels = c("T1", "T2")),
    Dimension = factor(Dimension, levels = tlx_dims)
  )

# --- Repeated Measures ANOVA per Dimension ---
cat("\\n=== Repeated Measures ANOVA (NASA-TLX) ===\\n")
for (dim_name in tlx_dims) {{
  subset_data <- long_data %>% filter(Dimension == dim_name)
  cat(sprintf("\\n--- %s ---\\n", dim_name))
  result <- ezANOVA(
    data = subset_data,
    dv = Score,
    wid = ParticipantID,
    within = Condition,
    detailed = TRUE
  )
  print(result$ANOVA)
}}

# --- Paired t-tests (with Bonferroni correction) ---
cat("\\n=== Paired t-tests (Bonferroni corrected, alpha = 0.05/6 = 0.0083) ===\\n")
for (dim_name in tlx_dims) {{
  t1_scores <- data[[paste0("T1_", dim_name)]]
  t2_scores <- data[[paste0("T2_", dim_name)]]
  tt <- t.test(t1_scores, t2_scores, paired = TRUE)
  cat(sprintf("%s: t=%.3f, p=%.4f %s\\n",
              dim_name, tt$statistic, tt$p.value,
              ifelse(tt$p.value < 0.0083, "*", "ns")))
}}

# --- Comparison Bar Chart ---
means <- long_data %>%
  group_by(Task, Dimension) %>%
  summarise(Mean = mean(Score), SD = sd(Score), .groups = "drop")

p <- ggplot(means, aes(x = Dimension, y = Mean, fill = Task)) +
  geom_bar(stat = "identity", position = position_dodge(width = 0.7), width = 0.6) +
  geom_errorbar(aes(ymin = Mean - SD, ymax = Mean + SD),
                position = position_dodge(width = 0.7), width = 0.2) +
  scale_fill_manual(values = c("A" = "#3498db", "B" = "#e74c3c"),
                    labels = c("A" = "Task A (Computer Assembly)",
                               "B" = "Task B (Fan Repair)")) +
  labs(title = "NASA-TLX Comparison: Task A vs Task B",
       subtitle = "Within-Subject Counterbalanced Design",
       x = "NASA-TLX Dimension", y = "Score (0-10)",
       fill = "Task Condition") +
  theme_minimal(base_size = 14) +
  theme(axis.text.x = element_text(angle = 30, hjust = 1),
        legend.position = "top")

ggsave("{os.path.join(os.path.dirname(output_csv_path) or '.', 'nasa_tlx_comparison.png').replace(chr(92), '/')}",
       plot = p, width = 12, height = 7, dpi = 150)

# --- Sickness Progression Plot ---
sickness_data <- data %>%
  select(ParticipantID, Task1_Condition, T1_Sickness, T2_Sickness) %>%
  mutate(
    Sickness_A = ifelse(Task1_Condition == "A", T1_Sickness, T2_Sickness),
    Sickness_B = ifelse(Task1_Condition == "A", T2_Sickness, T1_Sickness)
  ) %>%
  summarise(
    Mean_A = mean(Sickness_A), SD_A = sd(Sickness_A),
    Mean_B = mean(Sickness_B), SD_B = sd(Sickness_B),
    .groups = "drop"
  )

cat("\\n=== Sickness Summary ===\\n")
cat(sprintf("Task A: Mean=%.2f, SD=%.2f\\n", sickness_data$Mean_A, sickness_data$SD_A))
cat(sprintf("Task B: Mean=%.2f, SD=%.2f\\n", sickness_data$Mean_B, sickness_data$SD_B))

# --- Presence Comparison ---
cat("\\n=== Presence Scores ===\\n")
cat(sprintf("Task A Presence: Mean=%.2f\\n", mean(data$T1_Presence[data$Task1_Condition == "A"])))
cat(sprintf("Task B Presence: Mean=%.2f\\n", mean(data$T2_Presence[data$Task1_Condition == "A"])))

cat("\\n=== Analysis Complete! ===\\n")
cat("Plots saved alongside this script.\\n")
'''
    r_path = os.path.join(output_dir, 'analyze_rm_anova.R')
    with open(r_path, 'w', encoding='utf-8') as f:
        f.write(r_script)
    print(f"[DONE] R Script: {r_path}")
    return r_path


def generate_spss_syntax(output_csv_path, output_dir):
    """Auto-generate SPSS Syntax for Repeated Measures ANOVA."""
    csv_abs = os.path.abspath(output_csv_path).replace('\\', '/').replace('/', ':///')
    # SPSS wants forward slashes
    csv_spss = os.path.abspath(output_csv_path).replace('\\', '/')

    spss_syntax = f'''* ============================================================
 * VR Research: Repeated Measures ANOVA Analysis
 * Auto-generated by vr_offline_merger.py
 * Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}
 * ============================================================
 * How to run:
 *   1. Open SPSS
 *   2. File > Open > Syntax
 *   3. Select this .sps file
 *   4. Click green Play button (Run All)
 * ============================================================.

 * --- Import Data ---
 GET DATA /TYPE=TXT
   /FILE="{csv_spss}"
   /DELIIMIT=","
   /FIRSTCASE=2
   /VARIABLES=
     ParticipantID A10
     Age F3.0
     Gender A10
     VRExperience A15
     Task1_Condition A5
     Task2_Condition A5
     T1_Duration_Sec F8.2
     T1_Distance_M F8.2
     T1_AvgSpeed F8.2
     T1_GrabCount F3.0
     T1_ReleaseCount F3.0
     T1_TeleportCount F3.0
     T2_Duration_Sec F8.2
     T2_Distance_M F8.2
     T2_AvgSpeed F8.2
     T2_GrabCount F3.0
     T2_ReleaseCount F3.0
     T2_TeleportCount F3.0
     T1_MentalDemand F3.0
     T1_PhysicalDemand F3.0
     T1_TemporalDemand F3.0
     T1_Performance F3.0
     T1_Effort F3.0
     T1_Frustration F3.0
     T1_Presence F3.0
     T1_Sickness F3.0
     T2_MentalDemand F3.0
     T2_PhysicalDemand F3.0
     T2_TemporalDemand F3.0
     T2_Performance F3.0
     T2_Effort F3.0
     T2_Frustration F3.0
     T2_Presence F3.0
     T2_Sickness F3.0.
 EXECUTE.

 * --- Descriptive Statistics ---
 DESCRIBE VARIABLES=T1_MentalDemand T2_MentalDemand
   T1_PhysicalDemand T2_PhysicalDemand
   T1_TemporalDemand T2_TemporalDemand
   T1_Performance T2_Performance
   T1_Effort T2_Effort
   T1_Frustration T2_Frustration
   T1_Presence T2_Presence
   T1_Sickness T2_Sickness.

 * --- Repeated Measures ANOVA: Mental Demand ---
 GLM T1_MentalDemand T2_MentalDemand
   /WSFACTOR=Task 2
   /METHOD=SSTYPE(3)
   /PRINT=DESCRIPTIVE ETASQ HOMOGENEITY
   /CRITERIA=ALPHA(.05)
   /WSDESIGN=Task.

 * --- Repeated Measures ANOVA: Physical Demand ---
 GLM T1_PhysicalDemand T2_PhysicalDemand
   /WSFACTOR=Task 2
   /METHOD=SSTYPE(3)
   /PRINT=DESCRIPTIVE ETASQ
   /CRITERIA=ALPHA(.05)
   /WSDESIGN=Task.

 * --- Repeated Measures ANOVA: Temporal Demand ---
 GLM T1_TemporalDemand T2_TemporalDemand
   /WSFACTOR=Task 2
   /METHOD=SSTYPE(3)
   /PRINT=DESCRIPTIVE ETASQ
   /CRITERIA=ALPHA(.05)
   /WSDESIGN=Task.

 * --- Repeated Measures ANOVA: Performance ---
 GLM T1_Performance T2_Performance
   /WSFACTOR=Task 2
   /METHOD=SSTYPE(3)
   /PRINT=DESCRIPTIVE ETASQ
   /CRITERIA=ALPHA(.05)
   /WSDESIGN=Task.

 * --- Repeated Measures ANOVA: Effort ---
 GLM T1_Effort T2_Effort
   /WSFACTOR=Task 2
   /METHOD=SSTYPE(3)
   /PRINT=DESCRIPTIVE ETASQ
   /CRITERIA=ALPHA(.05)
   /WSDESIGN=Task.

 * --- Repeated Measures ANOVA: Frustration ---
 GLM T1_Frustration T2_Frustration
   /WSFACTOR=Task 2
   /METHOD=SSTYPE(3)
   /PRINT=DESCRIPTIVE ETASQ
   /CRITERIA=ALPHA(.05)
   /WSDESIGN=Task.

 * --- Paired t-tests (all NASA-TLX + Presence + Sickness) ---
 T-TEST PAIRS=T1_MentalDemand T1_PhysicalDemand T1_TemporalDemand
   T1_Performance T1_Effort T1_Frustration T1_Presence T1_Sickness
   WITH T2_MentalDemand T2_PhysicalDemand T2_TemporalDemand
   T2_Performance T2_Effort T2_Frustration T2_Presence T2_Sickness
   /PAIRED
   /CRITERIA=CI(.95)
   /MISSING=PAIRWISE.

 * --- Duration & Distance Paired t-tests ---
 T-TEST PAIRS=T1_Duration_Sec T1_Distance_M T1_GrabCount
   WITH T2_Duration_Sec T2_Distance_M T2_GrabCount
   /PAIRED
   /CRITERIA=CI(.95)
   /MISSING=PAIRWISE.

 * --- Order Effect Check: Independent Samples t-test ---
 * Compare Task 1 metrics between order groups (A->B vs B->A)
 RECODE Task1_Condition ('A'=1) ('B'=2) INTO OrderGroup.
 EXECUTE.
 T-TEST GROUPS=OrderGroup(1 2)
   /VARIABLES T1_Duration_Sec T1_MentalDemand T1_Sickness
   /CRITERIA=CI(.95)
   /MISSING=PAIRWISE.

 * --- Done ---
 MESSAGE 'Analysis complete. Check Output window for results.'.
'''
    spss_path = os.path.join(output_dir, 'analyze_rm_anova.sps')
    with open(spss_path, 'w', encoding='utf-8') as f:
        f.write(spss_syntax)
    print(f"[DONE] SPSS Syntax: {spss_path}")
    return spss_path


def merge_data(tablet_survey_path, quest_data_dir, output_path):
    """Main merge function with R/SPSS auto-generation."""
    print("\n" + "=" * 60)
    print("VR RESEARCH OFFLINE-TO-ONLINE DATA MERGER")
    print("=" * 60)

    print(f"\n[STEP 1] Loading tablet survey: {tablet_survey_path}")
    participants = load_tablet_survey(tablet_survey_path)

    print(f"\n[STEP 2] Analyzing Quest tracking data...")
    master_rows = []

    for pid, survey_data in participants.items():
        print(f"\n  Processing {pid}...")
        sessions = find_quest_sessions(quest_data_dir, pid)
        print(f"    Found {len(sessions)} session(s)")

        task1_metrics = {}
        task2_metrics = {}

        for session in sessions:
            has_task1 = any(f.name.startswith('task_1') for f in session.iterdir())
            has_task2 = any(f.name.startswith('task_2') for f in session.iterdir())

            if has_task1:
                task1_metrics = analyze_quest_session(str(session))
                print(f"    Task 1: Duration={task1_metrics.get('Duration_Sec', 0)}s, Distance={task1_metrics.get('Distance_M', 0)}m")
            elif has_task2:
                task2_metrics = analyze_quest_session(str(session))
                print(f"    Task 2: Duration={task2_metrics.get('Duration_Sec', 0)}s, Distance={task2_metrics.get('Distance_M', 0)}m")
            else:
                if not task1_metrics:
                    task1_metrics = analyze_quest_session(str(session))
                else:
                    task2_metrics = analyze_quest_session(str(session))

        row = {
            'ParticipantID': pid,
            'Age': survey_data['Age'],
            'Gender': survey_data['Gender'],
            'VRExperience': survey_data['VRExperience'],
            'Task1_Condition': survey_data['Task1_Condition'],
            'Task2_Condition': survey_data['Task2_Condition'],
            'T1_Duration_Sec': task1_metrics.get('Duration_Sec', 0),
            'T1_Distance_M': task1_metrics.get('Distance_M', 0),
            'T1_AvgSpeed': task1_metrics.get('AvgSpeed', 0),
            'T1_GrabCount': task1_metrics.get('GrabCount', 0),
            'T1_ReleaseCount': task1_metrics.get('ReleaseCount', 0),
            'T1_TeleportCount': task1_metrics.get('TeleportCount', 0),
            'T2_Duration_Sec': task2_metrics.get('Duration_Sec', 0),
            'T2_Distance_M': task2_metrics.get('Distance_M', 0),
            'T2_AvgSpeed': task2_metrics.get('AvgSpeed', 0),
            'T2_GrabCount': task2_metrics.get('GrabCount', 0),
            'T2_ReleaseCount': task2_metrics.get('ReleaseCount', 0),
            'T2_TeleportCount': task2_metrics.get('TeleportCount', 0),
            'T1_MentalDemand': survey_data['T1_MentalDemand'],
            'T1_PhysicalDemand': survey_data['T1_PhysicalDemand'],
            'T1_TemporalDemand': survey_data['T1_TemporalDemand'],
            'T1_Performance': survey_data['T1_Performance'],
            'T1_Effort': survey_data['T1_Effort'],
            'T1_Frustration': survey_data['T1_Frustration'],
            'T1_Presence': survey_data['T1_Presence'],
            'T1_Sickness': survey_data['T1_Sickness'],
            'T2_MentalDemand': survey_data['T2_MentalDemand'],
            'T2_PhysicalDemand': survey_data['T2_PhysicalDemand'],
            'T2_TemporalDemand': survey_data['T2_TemporalDemand'],
            'T2_Performance': survey_data['T2_Performance'],
            'T2_Effort': survey_data['T2_Effort'],
            'T2_Frustration': survey_data['T2_Frustration'],
            'T2_Presence': survey_data['T2_Presence'],
            'T2_Sickness': survey_data['T2_Sickness'],
        }
        master_rows.append(row)

    # Write master CSV
    output_dir = os.path.dirname(output_path) or '.'
    print(f"\n[STEP 3] Writing master CSV: {output_path}")
    if master_rows:
        headers = list(master_rows[0].keys())
        with open(output_path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.DictWriter(f, fieldnames=headers)
            writer.writeheader()
            writer.writerows(master_rows)
        print(f"[DONE] Written {len(master_rows)} rows to {output_path}")
    else:
        print("[WARN] No data to write.")

    # Auto-generate R Script and SPSS Syntax
    print(f"\n[STEP 4] Generating statistical analysis scripts...")
    generate_r_script(output_path, output_dir)
    generate_spss_syntax(output_path, output_dir)

    print(f"\n{'=' * 60}")
    print("PIPELINE COMPLETE!")
    print(f"{'=' * 60}")
    print(f"  Master CSV:    {output_path}")
    print(f"  R Script:      {os.path.join(output_dir, 'analyze_rm_anova.R')}")
    print(f"  SPSS Syntax:   {os.path.join(output_dir, 'analyze_rm_anova.sps')}")
    print(f"\nTo analyze:")
    print(f"  RStudio:    Open analyze_rm_anova.R > Source")
    print(f"  SPSS:       Open analyze_rm_anova.sps > Run All")

    return master_rows


def main():
    parser = argparse.ArgumentParser(description='VR Research Offline Data Merger')
    parser.add_argument('--tablet-survey', required=True, help='Path to external_tablet_survey.csv')
    parser.add_argument('--quest-data-dir', required=True, help='Directory containing Quest session folders')
    parser.add_argument('--output', default='master_research_database.csv', help='Output master CSV path')
    args = parser.parse_args()

    merge_data(args.tablet_survey, args.quest_data_dir, args.output)


if __name__ == '__main__':
    main()
