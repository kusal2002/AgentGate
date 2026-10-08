import { Link } from "react-router-dom";
export function NotFoundPage() {
  return (
    <div className="py-12">
      <h1 className="text-3xl font-semibold">Page not found</h1>
      <p className="mt-3 text-sm text-muted-foreground">
        This workspace page does not exist.
      </p>
      <Link className="mt-5 inline-block text-sm text-primary underline" to="/">
        Return to overview
      </Link>
    </div>
  );
}
