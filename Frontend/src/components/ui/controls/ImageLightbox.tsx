import { useEffect } from 'react';
import { ChevronLeft, ChevronRight, X } from 'lucide-react';
import { IconButton } from './IconButton';

export function ImageLightbox({
  isOpen,
  title,
  images,
  index,
  onIndexChange,
  onClose,
}: {
  isOpen: boolean;
  title?: string;
  images: Array<{ url: string; title?: string }>;
  index: number;
  onIndexChange: (next: number) => void;
  onClose: () => void;
}) {
  useEffect(() => {
    if (!isOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'ArrowLeft') onIndexChange(Math.max(0, index - 1));
      if (e.key === 'ArrowRight') onIndexChange(Math.min(images.length - 1, index + 1));
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isOpen, index, images.length, onClose, onIndexChange]);

  if (!isOpen) return null;
  const total = images.length;
  const current = Math.min(Math.max(index, 0), Math.max(total - 1, 0));
  const currentImage = images[current];

  return (
    <div
      className="fixed inset-0 z-[400] bg-black/80 flex items-center justify-center p-6"
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="relative w-full max-w-5xl">
        <div className="absolute -top-3 -right-3 z-20">
          <IconButton variant="overlay" size="sm" title="Close" onClick={onClose}>
            <X className="w-4 h-4" />
          </IconButton>
        </div>

        <div className="glass-panel-static-solid rounded-3xl border border-white/10 overflow-hidden">
          <div className="relative">
            <img
              src={currentImage?.url}
              alt={currentImage?.title ?? title ?? ''}
              className="block w-full max-h-[78vh] object-contain bg-black/20"
              draggable={false}
            />

            <div className="absolute inset-x-0 bottom-0 p-4 flex items-center justify-center gap-3">
              <IconButton
                variant="overlay"
                size="sm"
                title="Previous"
                onClick={() => onIndexChange(Math.max(0, current - 1))}
                disabled={current <= 0}
              >
                <ChevronLeft className="w-4 h-4" />
              </IconButton>

              <div className="px-3 py-2 rounded-2xl text-xs font-semibold bg-black/30 border border-white/10 text-white/80">
                {total === 0 ? '0/0' : `${current + 1}/${total}`}
              </div>

              <IconButton
                variant="overlay"
                size="sm"
                title="Next"
                onClick={() => onIndexChange(Math.min(total - 1, current + 1))}
                disabled={current >= total - 1}
              >
                <ChevronRight className="w-4 h-4" />
              </IconButton>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
