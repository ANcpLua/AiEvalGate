import fs from 'node:fs';

const file = process.argv[2];
if (!file) {
  console.error('usage: node scripts/validate-ai-review.mjs <review-output.json|md>');
  process.exit(2);
}

const raw = fs.readFileSync(file, 'utf8');
const start = raw.indexOf('{');
const end = raw.lastIndexOf('}');
if (start < 0 || end < start) {
  console.error('AI review output did not contain a JSON object.');
  process.exit(1);
}

const review = JSON.parse(raw.slice(start, end + 1));
const blocking = new Set(['P0', 'P1']);
const required = ['passed', 'score', 'severity', 'reviewers', 'findings'];

for (const key of required) {
  if (!(key in review)) {
    console.error(`AI review missing required key: ${key}`);
    process.exit(1);
  }
}

if (review.passed !== true) {
  console.error('AI review failed.');
  console.error(JSON.stringify(review, null, 2));
  process.exit(1);
}

if (typeof review.score !== 'number' || review.score < 0.85) {
  console.error(`AI review score ${review.score} is below 0.85.`);
  process.exit(1);
}

if (blocking.has(review.severity)) {
  console.error(`AI review returned blocking severity ${review.severity}.`);
  process.exit(1);
}

console.log('AI review gate passed.');
