/*
  Onboarding is where PRAXIS self-selects for difficulty (invention R-3). It does not sell comfort.
  The first thing it says out loud is the thing most products hide: it will tell you when you are
  failing. A learner who wants a streak and a dopamine hit should bounce off this screen — that is
  the screen working, not failing.

  The logic here is pure and framework-free so the gate that decides "can this person proceed" is
  testable without a browser. The rule is deliberately strict: you do not drift into a PRAXIS
  Objective. You pick a hard thing on purpose, and you affirm — in one click that cannot be
  defaulted-on — that you understand what you just asked for.
*/

/** The sentence the product is not afraid of. Single-sourced so the screen and its tests agree. */
export const HARD_TRUTH = "PRAXIS will tell you when you are failing.";

/** How much a target is honestly going to cost — stated before you choose, never after. */
export type Difficulty = "Serious" | "Hard" | "Punishing";

export type Objective = {
  /** The target concept in the Graph. Wired to a real Graph node when the Contract compiles (M11.4). */
  id: string;
  /** A role ontology, not a course title. "What do you want to become", not "what to watch". */
  title: string;
  /** One honest line on what the role actually is — the build target, not the brochure. */
  role: string;
  /** The cost, said plainly. This is the number everybody lies to themselves about. */
  cost: string;
  difficulty: Difficulty;
  /** Words a learner would actually type, so search finds the thing they mean by another name. */
  keywords: string[];
};

/*
  A curated Phase-0 catalog, not the 2,140 the prototype gestures at. Each is a genuinely hard
  build target — there is no "Intro to X" here on purpose. The full role-ontology graph is seeded
  in M13; until then these carry placeholder ids and no Objective is persisted before the Contract.
*/
export const OBJECTIVES: readonly Objective[] = [
  {
    id: "obj-ai-engineer",
    title: "AI Engineer",
    role: "Ship systems that put models in production and stay up. Not prompt-tinkering — the math, the serving, the failure modes.",
    cost: "Months. You will be wrong about transformers before you are right about them.",
    difficulty: "Hard",
    keywords: ["ai", "ml", "machine learning", "llm", "engineer", "transformers", "deep learning"],
  },
  {
    id: "obj-everyday-english",
    title: "Everyday English",
    role: "Write clear, natural everyday English — messages, plans, opinions — and kill the errors your first language plants. Text first: writing accuracy is the fuel speaking runs on.",
    cost: "Months of honest reps. You will find out what you only think you know.",
    difficulty: "Serious",
    keywords: ["english", "grammar", "writing", "vocabulary", "articles", "tenses", "fluency", "esl", "language"],
  },
  {
    id: "obj-reading-english",
    title: "Reading English",
    role: "Understand everyday and work English at reading speed — get the point, catch what's implied, and trust the right answer over the one that just repeats the words. The receiving side of fluency.",
    cost: "Months of passages you only half understand. The habit of stopping to look up every word is the first thing that has to die.",
    difficulty: "Serious",
    keywords: ["reading", "comprehension", "understanding", "english", "inference", "skimming", "esl", "work email", "language"],
  },
  {
    id: "obj-radiologist",
    title: "Diagnostic Radiologist",
    role: "Read what is on the film and what is not. The discipline is calibration — knowing exactly how sure you are.",
    cost: "Years. Every miss is a person. The bar does not move for you.",
    difficulty: "Punishing",
    keywords: ["radiology", "medicine", "diagnosis", "imaging", "doctor", "ct", "mri"],
  },
  {
    id: "obj-options-trader",
    title: "Options Trader",
    role: "Price uncertainty and take the other side of someone's fear. The edge is small, the arithmetic unforgiving.",
    cost: "The market grades you in cash, daily, and it does not care how you feel.",
    difficulty: "Punishing",
    keywords: ["options", "trading", "finance", "derivatives", "quant", "markets", "volatility"],
  },
  {
    id: "obj-systems-engineer",
    title: "Distributed Systems Engineer",
    role: "Build things that keep working when a machine dies mid-request. Consensus, consistency, the CAP you cannot cheat.",
    cost: "The bugs are non-deterministic. Your intuitions from single machines are lies here.",
    difficulty: "Hard",
    keywords: ["distributed", "systems", "backend", "consensus", "databases", "infrastructure", "scale"],
  },
  {
    id: "obj-statistician",
    title: "Applied Statistician",
    role: "Tell signal from noise and say how confident you are without lying. Causation is the whole game.",
    cost: "The easy answer is almost always the wrong one, and it will feel right.",
    difficulty: "Serious",
    keywords: ["statistics", "data", "inference", "probability", "causal", "bayesian", "analytics"],
  },
] as const;

/** Case- and whitespace-insensitive search across title, role, and the words learners actually use. */
export function searchObjectives(query: string): readonly Objective[] {
  const q = query.trim().toLowerCase();
  if (q === "") {
    return OBJECTIVES;
  }
  return OBJECTIVES.filter((o) => {
    const haystack = [o.title, o.role, ...o.keywords].join(" ").toLowerCase();
    return haystack.includes(q);
  });
}

/** The state the onboarding screen holds while the learner decides. */
export type OnboardingState = {
  selectedId: string | null;
  /** True only after an explicit, non-default affirmation of {@link HARD_TRUTH}. */
  acknowledgedHardTruth: boolean;
};

export const INITIAL_ONBOARDING: OnboardingState = {
  selectedId: null,
  acknowledgedHardTruth: false,
};

/**
 * The gate. You may not proceed to the Contract until you have chosen a real Objective **and**
 * affirmed what PRAXIS is. Neither defaults to true — self-selection has to be an act, not an
 * omission. An unknown id does not count as a choice.
 */
export function onboardingReady(state: OnboardingState): boolean {
  if (!state.acknowledgedHardTruth) {
    return false;
  }
  if (state.selectedId === null) {
    return false;
  }
  return OBJECTIVES.some((o) => o.id === state.selectedId);
}
