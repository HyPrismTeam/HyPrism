import { h, ComponentChildren, VNode, cloneElement, isValidElement } from 'preact';
import { useState, useEffect, useRef } from 'preact/hooks';

type MotionProps = {
  children?: ComponentChildren;
  initial?: Record<string, any> | string | boolean;
  animate?: Record<string, any> | string;
  exit?: Record<string, any> | string;
  variants?: Record<string, any>;
  transition?: {
    duration?: number;
    delay?: number;
    ease?: string | number[];
    type?: string;
    damping?: number;
    stiffness?: number;
    repeat?: number | string;
    repeatType?: string;
    times?: number[];
  };
  className?: string;
  style?: Record<string, any>;
  onClick?: (e: any) => void;
  [key: string]: any;
};

/** Resolve a variant key or object to a style object */
function resolveVariant(v: Record<string, any> | string | boolean | undefined, variants?: Record<string, any>): Record<string, any> {
  if (!v) return {};
  if (typeof v === 'boolean') return {};
  if (typeof v === 'string') return (variants && variants[v]) ? variants[v] : {};
  return v;
}

export const motion = {
  div: ({ children, initial, animate, exit: _exit, variants, transition, className, style, ...props }: MotionProps) => {
    const initStyle = resolveVariant(initial, variants);
    const animStyle = resolveVariant(animate, variants);
    const [currentStyle, setCurrentStyle] = useState({ ...initStyle, ...style });
    const isMounted = useRef(false);

    useEffect(() => {
      if (!isMounted.current) {
        isMounted.current = true;
        // Apply animate styles after a short delay to trigger CSS transition
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            setCurrentStyle({ ...style, ...animStyle });
          });
        });
      } else {
        setCurrentStyle({ ...style, ...animStyle });
      }
    }, [animate, style]);

    const transitionStyle = transition ? {
      transition: `all ${transition.duration || 0.3}s ${transition.ease || 'ease'} ${transition.delay || 0}s`
    } : {
      transition: 'all 0.3s ease'
    };

    return (
      <div className={className} style={{ ...currentStyle, ...transitionStyle }} {...props}>
        {children}
      </div>
    );
  },
  span: ({ children, initial, animate, exit: _exit, variants, transition, className, style, ...props }: MotionProps) => {
    const initStyle = resolveVariant(initial, variants);
    const animStyle = resolveVariant(animate, variants);
    const [currentStyle, setCurrentStyle] = useState({ ...initStyle, ...style });
    const isMounted = useRef(false);

    useEffect(() => {
      if (!isMounted.current) {
        isMounted.current = true;
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            setCurrentStyle({ ...style, ...animStyle });
          });
        });
      } else {
        setCurrentStyle({ ...style, ...animStyle });
      }
    }, [animate, style]);

    const transitionStyle = transition ? {
      transition: `all ${transition.duration || 0.3}s ${transition.ease || 'ease'} ${transition.delay || 0}s`
    } : {
      transition: 'all 0.3s ease'
    };

    return (
      <span className={className} style={{ ...currentStyle, ...transitionStyle }} {...props}>
        {children}
      </span>
    );
  },
  button: ({ children, initial, animate, exit: _exit, variants, transition, className, style, ...props }: MotionProps) => {
    const initStyle = resolveVariant(initial, variants);
    const animStyle = resolveVariant(animate, variants);
    const [currentStyle, setCurrentStyle] = useState({ ...initStyle, ...style });
    const isMounted = useRef(false);

    useEffect(() => {
      if (!isMounted.current) {
        isMounted.current = true;
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            setCurrentStyle({ ...style, ...animStyle });
          });
        });
      } else {
        setCurrentStyle({ ...style, ...animStyle });
      }
    }, [animate, style]);

    const transitionStyle = transition ? {
      transition: `all ${transition.duration || 0.3}s ${transition.ease || 'ease'} ${transition.delay || 0}s`
    } : {
      transition: 'all 0.3s ease'
    };

    return (
      <button className={className} style={{ ...currentStyle, ...transitionStyle }} {...props}>
        {children}
      </button>
    );
  }
};

export const AnimatePresence = ({ children, mode: _mode }: { children: ComponentChildren; mode?: string }) => {
  // A simplified AnimatePresence that just renders children for now.
  // Implementing full exit animations in Preact without a library is complex
  // and requires tracking child keys and delaying unmount.
  // For graceful degradation, we just render the children.
  return <>{children}</>;
};
