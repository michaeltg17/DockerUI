import { Check } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import { Button } from '@/components/ui/button';
import { Dialog } from '@/components/ui/dialog';
import { SearchBar } from '@/components/ui/search-bar';

import { AppIcon } from './app-icon';

type IconPickerDialogProps = {
  icons: string[];
  selected: string | null;
  onSelect: (icon: string | null) => void;
  onClose: () => void;
};

export const iconLabel = (path: string) => {
  const fileName = path.split('/').pop();
  return fileName ? fileName.replace(/\.[a-z0-9]+$/i, '') : path;
};

export const IconPickerDialog = ({
  icons,
  selected,
  onSelect,
  onClose,
}: IconPickerDialogProps) => {
  const [search, setSearch] = useState('');
  const searchRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    // The base dialog focuses the search box on open.
    searchRef.current?.focus();
  }, []);

  const visibleIcons = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (query.length === 0) return icons;
    return icons.filter((path) =>
      iconLabel(path).toLowerCase().includes(query),
    );
  }, [icons, search]);

  return (
    <Dialog
      title="Choose an icon"
      layered
      onClose={onClose}
      className="flex max-h-[80vh] max-w-2xl flex-col"
    >
      <div className="flex items-center gap-2 border-b border-border p-3">
        <SearchBar
          ref={searchRef}
          value={search}
          onChange={setSearch}
          label="Search icons by name"
          className="min-w-0 flex-1"
        />
        <Button type="button" variant="outline" onClick={() => onSelect(null)}>
          None
        </Button>
      </div>

      <div className="grid flex-1 grid-cols-3 content-start gap-2 overflow-auto p-4 sm:grid-cols-4">
        {visibleIcons.map((path) => (
          <button
            key={path}
            type="button"
            onClick={() => onSelect(path)}
            title={iconLabel(path)}
            className="relative flex flex-col items-center gap-2 rounded-lg p-2 transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
          >
            <AppIcon
              icon={path}
              name={iconLabel(path)}
              className="size-24 text-3xl"
            />
            <span className="max-w-full truncate text-xs text-muted-foreground">
              {iconLabel(path)}
            </span>
            {selected === path && (
              <span className="absolute right-1 top-1 flex size-5 items-center justify-center rounded-full bg-primary text-primary-foreground">
                <Check className="size-3" aria-hidden="true" />
              </span>
            )}
          </button>
        ))}
        {visibleIcons.length === 0 && (
          <p className="col-span-full py-8 text-center text-sm text-muted-foreground">
            No icons match &ldquo;{search.trim()}&rdquo;.
          </p>
        )}
      </div>
    </Dialog>
  );
};
