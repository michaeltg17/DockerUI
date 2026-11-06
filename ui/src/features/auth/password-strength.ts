export type PasswordStrength = 'weak' | 'medium' | 'strong';

const LOWERCASE = /[a-z]/;
const UPPERCASE = /[A-Z]/;
const DIGIT = /\d/;
const SYMBOL = /[^a-zA-Z0-9]/;

/**
 * Rates a password as weak, medium, or strong: the length band (under 8
 * characters scores 0, 8–11 scores 1, 12–15 scores 2, 16 or more scores 3)
 * plus the number of character classes present (lowercase, uppercase, digit,
 * symbol; 0–4). A total of 3 or fewer — or under 8 characters — is weak,
 * 4–5 is medium, and 6 or more is strong. Informational only: it never blocks
 * the save (the API accepts any non-empty password).
 */
export const assessPasswordStrength = (password: string): PasswordStrength => {
  const lengthScore =
    password.length >= 16
      ? 3
      : password.length >= 12
        ? 2
        : password.length >= 8
          ? 1
          : 0;

  const classScore = [LOWERCASE, UPPERCASE, DIGIT, SYMBOL].filter((pattern) =>
    pattern.test(password),
  ).length;

  const total = lengthScore + classScore;

  if (password.length < 8 || total <= 3) return 'weak';
  if (total <= 5) return 'medium';
  return 'strong';
};
