import { Search, X } from 'lucide-react';
import { forwardRef, useRef } from 'react';

import { cn } from '@/utils/cn';

const variants = {
  header: {
    input:
      'bg-card pl-9 shadow-sm focus:border-ring focus:outline-none focus:ring-2 focus:ring-ring/30',
    icon: 'left-3',
  },
  dialog: {
    input:
      'bg-background pl-8 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring',
    icon: 'left-2.5',
  },
};

export type SearchBarProps = {
  value: string;
  onChange: (value: string) => void;
  label: string;
  placeholder?: string;
  variant?: keyof typeof variants;
  className?: string;
};

export const SearchBar = forwardRef<HTMLInputElement, SearchBarProps>(
  (
    {
      value,
      onChange,
      label,
      placeholder = label,
      variant = 'dialog',
      className = '',
    },
    ref,
  ) => {
    const inputRef = useRef<HTMLInputElement | null>(null);
    const hasValue = value.trim().length > 0;

    return (
      <div className={cn('relative', className)}>
        <Search
          className={cn(
            'pointer-events-none absolute top-1/2 size-4 -translate-y-1/2 text-muted-foreground',
            variants[variant].icon,
          )}
          aria-hidden="true"
        />
        <input
          ref={(node) => {
            inputRef.current = node;
            if (typeof ref === 'function') {
              ref(node);
            } else if (ref) {
              ref.current = node;
            }
          }}
          type="search"
          aria-label={label}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
          className={cn(
            'h-9 w-full rounded-md border border-input pr-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground',
            variants[variant].input,
            hasValue && 'pr-8',
          )}
        />
        {hasValue && (
          <button
            type="button"
            onClick={() => {
              onChange('');
              inputRef.current?.focus();
            }}
            aria-label="Clear search"
            className="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          >
            <X className="size-4" aria-hidden="true" />
          </button>
        )}
      </div>
    );
  },
);

SearchBar.displayName = 'SearchBar';
